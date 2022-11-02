using AccountsData.Models.DataModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VLO_BOARDS.Extensions;
using File = AccountsData.Models.DataModels.File;
using Index = Meilisearch.Index;

namespace vlo_main.Areas.Article;

public partial class ArticleController : ControllerBase
{
    [Route("AddComment")]
    [HttpPost]
    public async Task<ActionResult> AddComment(string content, string ArticleId)
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
        
        article.Comments.Add(new Comment
        {
            Author = user,
            Content = content,
        });

        await _db.SaveChangesAsync();

        return Ok();
    }
    
    [Route("AddReaction")]
    [HttpPost]
    public async Task<ActionResult> AddReaction(ReactionType reactionType, string ArticleId)
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

        var reaction = new Reaction { User = user, ReactionType = reactionType };
        
        if (article.Reactions.Contains(reaction))
        {
            article.Reactions.Remove(reaction);
            await _db.SaveChangesAsync();
        }
        
        article.Reactions.Add(reaction);

        await _db.SaveChangesAsync();

        return Ok();
    }
}