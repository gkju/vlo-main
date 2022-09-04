using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations;
using AccountsData.Data;
using AccountsData.Models.DataModels;
using Amazon.S3;
using Meilisearch;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VLO_BOARDS.Extensions;
using File = AccountsData.Models.DataModels.File;
using Index = Meilisearch.Index;

namespace vlo_main.Areas.Article;

[ApiController]
[Area("Articles")]
[Route("api/[area]/[controller]")]
[Authorize]
public class ArticleController : ControllerBase
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ApplicationDbContext _db;
    private readonly IServiceProvider _serviceProvider;
    private readonly MinioConfig _minioConfig;
    private readonly AmazonS3Client _minioClient;
    private readonly MeilisearchClient _meilisearchClient;

    private Index _index => _meilisearchClient.Index("Articles");

    public ArticleController(
        UserManager<ApplicationUser> userManager, 
        ApplicationDbContext db,
        IServiceProvider sp,
        MinioConfig mconf,
        AmazonS3Client mc,
        MeilisearchClient meilisearchClient)
    {
        _userManager = userManager;
        _db = db;
        _serviceProvider = sp;
        _minioConfig = mconf;
        _minioClient = mc;
        _meilisearchClient = meilisearchClient;
    }

    /// <summary>
    /// Creates a blank article and returns its ID
    /// </summary>
    /// <returns></returns>
    [HttpPost]
    public async Task<IActionResult> OnPostAsync()
    {
        var user = await _userManager.GetUserAsync(User);
        var article = new AccountsData.Models.DataModels.Article
        {
            Title = "",
            ContentJson = "",
            Author = user,
            ModifiedOn = DateTime.Now.ToUniversalTime(),
            Picture = new File
            {
                ObjectId = Guid.NewGuid().ToString(),
                UserManageable = false,
                Owner = user,
                ByteSize = 0,
                Bucket = "",
                BackedInMinio = false,
                ContentType = "",
                FileName= "dummy picture"
            }
        };

        await _index.AddDocumentsAsync<MeiliArticle>(new List<MeiliArticle> {
            article.GetMeiliArticle()
        });

        _db.Articles.Add(article);
        await _db.SaveChangesAsync();
        
        return Ok(article.ArticleId);
    }
    
    [Route("SetTitle")]
    [HttpPut]
    public async Task<IActionResult> SetTitle(ArticleTitleInput input)
    {
        var article = await _db.Articles.FindAsync(input.ArticleId);
        if (article is null)
        {
            ModelState.AddModelError(Constants.ArticleError, Constants.NotFoundCode);
            return this.GenBadRequestProblem();
        }

        var user = await _userManager.GetUserAsync(User);

        if (!article.UserCanEdit(user))
        {
            ModelState.AddModelError(Constants.ArticleError, Constants.CannotEditCode);
            return this.GenBadRequestProblem();
        }
        
        article.Title = input.Title;
        article.ModifiedOn = DateTime.Now.ToUniversalTime();
        await _index.UpdateDocumentsAsync<MeiliArticle>(new List<MeiliArticle> {
            article.GetMeiliArticle()
        });
        await _db.SaveChangesAsync();
        return Ok();
    }
    
    public class ArticleTitleInput
    {
        [Required]
        public string ArticleId { get; set; }
        [Required]
        public string Title { get; set; }
    }
    
    [HttpPut]
    public async Task<IActionResult> OnPutAsync(ArticleUpdateInput input)
    {
        var article = await _db.Articles.Where(a => a.ArticleId == input.ArticleId).Include(a => a.Revisions).ThenInclude(r => r.Author).FirstOrDefaultAsync();
        if (article is null)
        {
            ModelState.AddModelError(Constants.ArticleError, Constants.NotFoundCode);
            return this.GenBadRequestProblem();
        }

        var user = await _userManager.GetUserAsync(User);

        if (!article.UserCanEdit(user))
        {
            ModelState.AddModelError(Constants.ArticleError, Constants.CannotEditCode);
            return this.GenBadRequestProblem();
        }
        
        article.ContentJson = input.ContentJson;
        await article.HandleRevision(input.ContentJson, user, _db);
        article.ModifiedOn = DateTime.Now.ToUniversalTime();
        await _db.SaveChangesAsync();
        await _index.UpdateDocumentsAsync<MeiliArticle>(new List<MeiliArticle> {
            article.GetMeiliArticle()
        });
        return Ok(article.ArticleId);
    }
    
    [Route("AddEditor")]
    [HttpPut]
    public async Task<IActionResult> AddEditor(UserArticleInput input)
    {
        var article = await _db.Articles
            .Include(a => a.Editors)
            .FirstOrDefaultAsync(a => a.ArticleId == input.ArticleId);
        var user = await _userManager.GetUserAsync(User);
        if (article is null || user is null)
        {
            ModelState.AddModelError(Constants.ArticleError, Constants.NotFoundCode);
            return this.GenBadRequestProblem();
        }
        if (!article.UserCanEdit(user))
        {
            ModelState.AddModelError(Constants.ArticleError, Constants.CannotEditCode);
            return this.GenBadRequestProblem();
        }
        
        var editor = await _db.Users.FirstOrDefaultAsync(u => u.Id == input.UserId);
        if (editor is null)
        {
            ModelState.AddModelError(Constants.ArticleError, Constants.NotFoundUserCode);
            return this.GenBadRequestProblem();
        }

        if (!article.Editors.Contains(editor))
        {
            article.Editors.Add(editor);
            await _db.SaveChangesAsync();
            return Ok(editor.UserName);
        }
        
        ModelState.AddModelError(Constants.ArticleError, Constants.AlreadyAddedCode);
        return this.GenBadRequestProblem();
    }

    [Route("RemoveEditor")]
    [HttpDelete]
    public async Task<ActionResult> RemoveEditor(UserArticleInput input)
    {
        var article = await _db.Articles
            .Include(a => a.Editors)
            .Include(a => a.Reviewers)
            .FirstOrDefaultAsync(a => a.ArticleId == input.ArticleId);
        var user = await _userManager.GetUserAsync(User);
        if (article is null || user is null)
        {
            ModelState.AddModelError(Constants.ArticleError, Constants.NotFoundCode);
            return this.GenBadRequestProblem();
        }
        if (!article.UserCanEdit(user))
        {
            ModelState.AddModelError(Constants.ArticleError, Constants.CannotEditCode);
            return this.GenBadRequestProblem();
        }
        
        var editor = await _db.Users.FirstOrDefaultAsync(u => u.Id == input.UserId);
        if (editor is null)
        {
            ModelState.AddModelError(Constants.ArticleError, Constants.NotFoundUserCode);
            return this.GenBadRequestProblem();
        }
        
        if (!article.Editors.Contains(editor))
        {
            ModelState.AddModelError(Constants.ArticleError, Constants.NotFoundUserCode);
            return this.GenBadRequestProblem();
        }
        
        article.Editors.Remove(editor);
        await _db.SaveChangesAsync();
        return Ok(editor.Id);
    }

    [Route("SetPicture")]
    [HttpPost]
    public async Task<ActionResult> SetPicture([FromForm] FileArticleInput input)
    {
        var article = await _db.Articles
            .Include(a => a.Editors)
            .Include(a => a.Reviewers)
            .Include(a => a.Picture.Owner)
            .FirstOrDefaultAsync(a => a.ArticleId == input.ArticleId);
        var user = await _userManager.GetUserAsync(User);
        if (article is null || user is null)
        {
            ModelState.AddModelError(Constants.ArticleError, Constants.NotFoundCode);
            return this.GenBadRequestProblem();
        }
        if (!article.UserCanEdit(user))
        {
            ModelState.AddModelError(Constants.ArticleError, Constants.CannotEditCode);
            return this.GenBadRequestProblem();
        }

        var id = await user.UploadFile(input.File, _serviceProvider, _minioConfig.BucketName, false, false);

        article.Picture.UserManageable = true;
        var objectId = article.Picture.ObjectId;
        article.Picture = await _db.Files.FirstOrDefaultAsync(f => f.ObjectId == id);
        await article.Picture.Owner.DeleteFile(objectId, _serviceProvider);

        await _db.SaveChangesAsync();
        
        return Ok(article.Picture.ByteSize);
    }

    public class ArticleDateInput
    {
        public DateTime PublishOn { get; set; }
        public string ArticleId { get; set; }
    }

    [Route("SetPublishDate")]
    [HttpPut]
    public async Task<ActionResult> SetPublishOn(ArticleDateInput input)
    {
        var article = await _db.Articles
            .Include(a => a.Editors)
            .Include(a => a.Reviewers)
            .Include(a => a.Picture.Owner)
            .FirstOrDefaultAsync(a => a.ArticleId == input.ArticleId);
        var user = await _userManager.GetUserAsync(User);
        if (article is null || user is null)
        {
            ModelState.AddModelError(Constants.ArticleError, Constants.NotFoundCode);
            return this.GenBadRequestProblem();
        }
        if (!article.UserCanEdit(user))
        {
            ModelState.AddModelError(Constants.ArticleError, Constants.CannotEditCode);
            return this.GenBadRequestProblem();
        }

        article.AutoPublishOn = input.PublishOn;
        await _db.SaveChangesAsync();
        
        return Ok(article.AutoPublishOn);
    }

    public class ArticleBoolInput
    {
        public string ArticleId { get; set; }
        public bool Public { get; set; }
    }
    
    [Route("SetPublic")]
    [HttpPut]
    public async Task<ActionResult> SetPublic(ArticleBoolInput input)
    {
        var article = await _db.Articles
            .Include(a => a.Editors)
            .Include(a => a.Reviewers)
            .Include(a => a.Picture.Owner)
            .FirstOrDefaultAsync(a => a.ArticleId == input.ArticleId);
        var user = await _userManager.GetUserAsync(User);
        if (article is null || user is null)
        {
            ModelState.AddModelError(Constants.ArticleError, Constants.NotFoundCode);
            return this.GenBadRequestProblem();
        }
        if (!article.UserCanEdit(user))
        {
            ModelState.AddModelError(Constants.ArticleError, Constants.CannotEditCode);
            return this.GenBadRequestProblem();
        }

        article.Public = input.Public;
        await _db.SaveChangesAsync();
        
        return Ok(article.AutoPublishOn);
    }

    [Route("GetPicture")]
    [HttpGet]
    public async Task<ActionResult> GetPicture(string ArticleId)
    {
        var article = await _db.Articles
            .Include(a => a.Editors)
            .Include(a => a.Reviewers)
            .Include(a => a.Picture)
            .FirstOrDefaultAsync(a => a.ArticleId == ArticleId);
        var user = await _userManager.GetUserAsync(User);
        if (article is null || user is null)
        {
            ModelState.AddModelError(Constants.ArticleError, Constants.NotFoundCode);
            return this.GenBadRequestProblem();
        }
        if(!article.UserCanView(user))
        {
            ModelState.AddModelError(Constants.ArticleError, Constants.CannotViewCode);
            return this.GenBadRequestProblem();
        }

        if (!article.Picture.BackedInMinio)
        {
            ModelState.AddModelError(Constants.ArticleError, Constants.NoPictureCode);
            return this.GenNotFound();
        }

        return Ok(article.Picture.GetSignedUrl(_minioClient));
    }

    [Route("GetContent")]
    [HttpGet]
    public async Task<ActionResult> GetContent(string ArticleId)
    {
        var article = await _db.Articles
            .Include(a => a.Editors)
            .Include(a => a.Reviewers)
            .FirstOrDefaultAsync(a => a.ArticleId == ArticleId);
        var user = await _userManager.GetUserAsync(User);
        if (article is null || user is null)
        {
            ModelState.AddModelError(Constants.ArticleError, Constants.NotFoundCode);
            return this.GenBadRequestProblem();
        }
        if(!article.UserCanView(user))
        {
            ModelState.AddModelError(Constants.ArticleError, Constants.CannotViewCode);
            return this.GenBadRequestProblem();
        }

        return Ok(article.ContentJson);
    }

    [Route("GetArticle")]
    [HttpGet]
    [ProducesResponseType(typeof(AccountsData.Models.DataModels.Article), StatusCodes.Status200OK)]
    public async Task<ActionResult> GetArticle(string ArticleId)
    {
        var article = await _db.Articles
            .Include(a => a.Editors)
            .Include(a => a.Reviewers)
            .FirstOrDefaultAsync(a => a.ArticleId == ArticleId);
        var user = await _userManager.GetUserAsync(User);
        if (article is null || user is null)
        {
            ModelState.AddModelError(Constants.ArticleError, Constants.NotFoundCode);
            return this.GenBadRequestProblem();
        }
        if(!article.UserCanView(user))
        {
            ModelState.AddModelError(Constants.ArticleError, Constants.CannotViewCode);
            return this.GenBadRequestProblem();
        }

        return Ok(article);
    }
    
    [Route("GetTags")]
    [HttpGet]
    [ProducesResponseType(typeof(Tag[]), StatusCodes.Status200OK)]
    public async Task<ActionResult> GetTags(string ArticleId)
    {
        var article = await _db.Articles
            .Include(a => a.Editors)
            .Include(a => a.Reviewers)
            .Include(a => a.Tags)
            .FirstOrDefaultAsync(a => a.ArticleId == ArticleId);
        var user = await _userManager.GetUserAsync(User);
        if (article is null || user is null)
        {
            ModelState.AddModelError(Constants.ArticleError, Constants.NotFoundCode);
            return this.GenBadRequestProblem();
        }
        if(!article.UserCanView(user))
        {
            ModelState.AddModelError(Constants.ArticleError, Constants.CannotViewCode);
            return this.GenBadRequestProblem();
        }

        return Ok(article.Tags);
    }

    [Route("SearchArticles")]
    [HttpGet]
    public async Task<ActionResult> SearchArticles([Required] string query)
    {
        IReadOnlyCollection<MeiliArticle> articleCandidates;
        IEnumerable<string> idCandidates = new List<string>();
        try
        {
            articleCandidates = (await _index.SearchAsync<MeiliArticle>(query, new SearchQuery() { Limit = 100 })).Hits;
            idCandidates = articleCandidates.Select(a => a.Id);
        }
        catch
        {
            
        }
        

        var articles = (await _db.Articles
                .Include(a => a.Editors)
                .Include(a => a.Reviewers)
                .Where(a => idCandidates.Contains(a.ArticleId) || a.Title.Contains(query) ||
                            a.ContentText.Contains(query) || a.Author.UserName.Contains(query))
                .ToListAsync())
            .Where(a => a.IsPublic())
            .Take(100)
            .ToList();

        return Ok(articles);
    }
}

public class FileArticleInput {
    [Required]
    public string ArticleId { get; set; }
    [Required]
    public IFormFile File { get; set; }
}

public class UserArticleInput
{
    [Required]
    public string ArticleId { get; set; }
    [Required]
    public string UserId { get; set; }
}

public class ArticleUpdateInput
{
    [Required]
    public string ArticleId { get; set; }
    [Required]
    public string ContentJson { get; set; }
}