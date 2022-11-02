using AccountsData.Data;
using AccountsData.Models.DataModels;
using Amazon.S3;
using Meilisearch;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VLO_BOARDS.Extensions;
using Index = Meilisearch.Index;

namespace vlo_main.Areas.Article;

[ApiController]
[Area("Tags")]
[Route("api/[area]/[controller]")]
[Authorize]
public class TagController : ControllerBase
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ApplicationDbContext _db;
    private readonly IServiceProvider _serviceProvider;
    private readonly MinioConfig _minioConfig;
    private readonly AmazonS3Client _minioClient;
    private readonly MeilisearchClient _meilisearchClient;

    private Index _index => _meilisearchClient.Index("Tags");

    public TagController(
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

    [HttpPost]
    public async Task<ActionResult> OnPostAsync(string tagContent)
    {
        var tag = Tag.NormalizeTagString(tagContent);
        var tagExists = await _db.Tags.AnyAsync(t => t.Content == tag);
        if (tagExists)
        {
            ModelState.AddModelError(Constants.TagError, Constants.TagExists);
            return this.GenBadRequestProblem();
        }
        
        var newTag = new Tag
        {
            Content = tag,
            Author = await _userManager.GetUserAsync(User)
        };

        await _db.Tags.AddAsync(newTag);
        await _index.AddDocumentsAsync(new List<Tag> {newTag});
        await _db.SaveChangesAsync();
        return Ok(newTag.Content);
    }

    [Route("AddToArticle")]
    [HttpPost]
    public async Task<ActionResult> AddToArticle(string articleId, string tagContent)
    {
        var tag = Tag.NormalizeTagString(tagContent);
        var dbTag = await _db.Tags.FirstOrDefaultAsync(t => t.Content == tag);
        if (dbTag == null)
        {
            ModelState.AddModelError(Constants.TagError, Constants.TagNotFound);
            return this.GenBadRequestProblem();
        }
        
        var article = await _db.Articles
            .Include(a => a.Editors)
            .Include(a => a.Reviewers)
            .Include(a => a.Picture.Owner)
            .FirstOrDefaultAsync(a => a.ArticleId == articleId);
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

        if (article.Tags.Contains(dbTag))
        {
            ModelState.AddModelError(Constants.ArticleError, Constants.AlreadyAddedTagCode);
            return this.GenBadRequestProblem();
        }
        
        article.Tags.Add(dbTag);
        await _db.SaveChangesAsync();
        return Ok(article.Tags);
    }
    
    [Route("RemoveFromArticle")]
    [HttpDelete]
    public async Task<ActionResult> RemoveFromArticle(string articleId, string tagContent)
    {
        var tag = Tag.NormalizeTagString(tagContent);
        var article = await _db.Articles
            .Include(a => a.Editors)
            .Include(a => a.Reviewers)
            .Include(a => a.Tags)
            .Include(a => a.Picture.Owner)
            .FirstOrDefaultAsync(a => a.ArticleId == articleId);
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
        
        var dbTag = article.Tags.FirstOrDefault(t => t.Content == tag);
        if (dbTag == null)
        {
            ModelState.AddModelError(Constants.TagError, Constants.TagNotFound);
            return this.GenBadRequestProblem();
        }
        
        article.Tags.Remove(dbTag);
        await _db.SaveChangesAsync();
        return Ok(article.Tags);
    }
    
    [HttpGet]
    [Route("Search")]
    public async Task<ActionResult> Search(string query = "")
    {
        IReadOnlyCollection<Tag> tagCandidates;
        IEnumerable<string> idCandidates = new List<string>();
        try
        {
            tagCandidates = (await _index.SearchAsync<Tag>(query, new SearchQuery() { Limit = 100 })).Hits;
            idCandidates = tagCandidates.Select(a => a.Id);
        }
        catch
        {
            
        }


        var tags = (await _db.Tags
            .Where(a => idCandidates.Contains(a.Id) || a.Content.Contains(query))
            .Take(100)
            .ToListAsync());

        return Ok(tags);
        
    }
}