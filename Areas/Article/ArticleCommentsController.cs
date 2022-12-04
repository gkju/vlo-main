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
            .Include(a => a.Comments)
            .FirstOrDefaultAsync(a => a.ArticleId == ArticleId);
        var userProto = await _userManager.GetUserAsync(User);
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userProto.Id);
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
    
    [Route("AddReplyArticle")]
    [HttpPost]
    public async Task<ActionResult> AddReply(string content, string commentId, string articleId)
    {
        var article = await _db.Articles
            .Include(a => a.Editors)
            .Include(a => a.Reviewers)
            .Include(a => a.Comments)
            .FirstOrDefaultAsync(a => a.ArticleId == articleId);
        var comment = article?.Comments.FirstOrDefault(c => c.Id == commentId);
        var userProto = await _userManager.GetUserAsync(User);
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userProto.Id);
        if (article is null || comment is null || user is null)
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
            InReplyTo = comment
        });

        await _db.SaveChangesAsync();

        return Ok();
    }
    
    [Route("DeleteArticleComment")]
    [HttpDelete]
    public async Task<ActionResult> DeleteArticleComment(string commentId, string articleId)
    {
        var article = await _db.Articles
            .Include(a => a.Editors)
            .Include(a => a.Reviewers)
            .Include(a => a.Comments)
            .ThenInclude(c => c.Reactions)
            .FirstOrDefaultAsync(a => a.ArticleId == articleId);
        var comment = article?.Comments.FirstOrDefault(c => c.Id == commentId);
        var userProto = await _userManager.GetUserAsync(User);
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userProto.Id);
        if (article is null || comment is null || user is null)
        {
            ModelState.AddModelError(Constants.ArticleError, Constants.NotFoundCode);
            return this.GenBadRequestProblem();
        }
        if(!article.UserCanView(user))
        {
            ModelState.AddModelError(Constants.ArticleError, Constants.CannotViewCode);
            return this.GenBadRequestProblem();
        }
        
        if(comment.Author != user)
        {
            ModelState.AddModelError(Constants.ArticleError, Constants.CannotDeleteCode);
            return this.GenBadRequestProblem();
        }

        article.Comments.Remove(comment);
        _db.Reactions.RemoveRange(comment.Reactions);
        _db.Comments.Remove(comment);

        await _db.SaveChangesAsync();

        return Ok();
    }
    
    [Route("AddReactionToComment")]
    [HttpPost]
    public async Task<ActionResult> AddReactionToComment(ReactionType reactionType, string ArticleId, string commentId)
    {
        var article = await _db.Articles
            .Include(a => a.Editors)
            .Include(a => a.Reviewers)
            .Include(a => a.Comments)
            .ThenInclude(c => c.Reactions)
            .Include(a => a.Reactions)
            .ThenInclude(r => r.User)
            .FirstOrDefaultAsync(a => a.ArticleId == ArticleId);
        var comment = article?.Comments.FirstOrDefault(c => c.Id == commentId);
        var userProto = await _userManager.GetUserAsync(User);
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userProto.Id);
        if (comment is null || article is null || user is null)
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
        
        if (comment.Reactions.Count(r => r.User.Id == user.Id) > 0)
        {
            var reactions = comment.Reactions.Where(r => r.User.Id == user.Id);
            _db.Reactions.RemoveRange(reactions);
            await _db.SaveChangesAsync();
        }
        
        comment.Reactions.Add(reaction);

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
            .Include(a => a.Reactions)
            .ThenInclude(r => r.User)
            .FirstOrDefaultAsync(a => a.ArticleId == ArticleId);
        var userProto = await _userManager.GetUserAsync(User);
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userProto.Id);
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
        
        if (article.Reactions.Count(r => r.User.Id == user.Id) > 0)
        {
            var reactions = article.Reactions.Where(r => r.User.Id == user.Id);
            _db.Reactions.RemoveRange(reactions);
            await _db.SaveChangesAsync();
        }
        
        article.Reactions.Add(reaction);

        await _db.SaveChangesAsync();

        return Ok();
    }
}