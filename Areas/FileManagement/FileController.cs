using System.Net;
using AccountsData.Data;
using AccountsData.Models.DataModels;
using Amazon.S3;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VLO_BOARDS.Extensions;
using vlo_main.Areas.Article;
using File = AccountsData.Models.DataModels.File;

namespace vlo_main.Areas.FileManagement;

[ApiController]
[Area("FileManagement")]
[Route("api/[area]/[controller]")]
[Authorize]
public class FileController : ControllerBase
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ApplicationDbContext _db;
    private readonly IServiceProvider _serviceProvider;
    private readonly MinioConfig _minioConfig;
    private readonly AmazonS3Client _minioClient;

    public FileController(
        UserManager<ApplicationUser> userManager, 
        ApplicationDbContext db,
        IServiceProvider sp,
        MinioConfig mconf,
        AmazonS3Client mc)
    {
        _userManager = userManager;
        _db = db;
        _serviceProvider = sp;
        _minioConfig = mconf;
        _minioClient = mc;
    }
    
    // flat file/folder hierarchy, every folder has assigned children folder for creating a hierarchy in a client app
    
    [Route("GetUserFoldersFiles")]
    [HttpGet]
    public async Task<ActionResult> GetUserFoldersFiles()
    {
        var userProto = await _userManager.GetUserAsync(User);
        var user = await _db.Users
            .Where(u => u.Id == userProto.Id)
            .Include(u => u.Folders)
                .ThenInclude(f => f.Files)
            .Include(u => u.Folders)
            .Include(a => a.Files)
            .FirstOrDefaultAsync(u => u.Id == userProto.Id);

        if (user is null)
        {
           throw new Exception("User with a claimsprincipal not found"); 
        }

        return Ok(new FoldersFiles(user.Folders, user.Files));
    }

    public class FoldersFiles
    {
        public List<Folder> Folders { get; set; }
        public List<File> Files { get; set; }
        public FoldersFiles(List<Folder> folders, List<File> files)
        {
            Folders = folders;
            Files = files;
        }
    }
    
    [Route("CreateFolder")]
    [HttpPost]
    
    public async Task<ActionResult> CreateFolder(string name)
    {
        var userProto = await _userManager.GetUserAsync(User);
        var user = await _db.Users
            .Where(u => u.Id == userProto.Id)
            .Include(u => u.Folders)
            .ThenInclude(f => f.Files)
            .Include(u => u.Folders)
            .FirstOrDefaultAsync(u => u.Id == userProto.Id);

        if (user is null)
        {
            throw new Exception("User with a claimsprincipal not found"); 
        }

        var folder = new Folder
        {
            Name = name,
        };
        
        user.Folders.Add(folder);
        await _db.SaveChangesAsync();

        return Ok(folder.Id);
    }
    
    [Route("AddSubFolder")]
    [HttpPost]
    
    public async Task<ActionResult> AddSubFolder(string ParentId, string ChildId)
    {
        if (ParentId == ChildId)
        {
            ModelState.AddModelError(Constants.FileError, Constants.CannotAddIllegalCode);
            return this.GenBadRequestProblem();
        }
        
        var userProto = await _userManager.GetUserAsync(User);
        var user = await _db.Users
            .Where(u => u.Id == userProto.Id)
            .Include(u => u.Folders)
            .ThenInclude(f => f.Files)
            .Include(u => u.Folders)
            .FirstOrDefaultAsync(u => u.Id == userProto.Id);

        if (user is null)
        {
            throw new Exception("User with a claimsprincipal not found"); 
        }
        
        var parentFolder = await _db.Folders.FirstOrDefaultAsync(f => f.Id == ParentId);
        var childFolder = await _db.Folders.FirstOrDefaultAsync(f => f.Id == ChildId);
        if(parentFolder is null || childFolder is null)
        {
            ModelState.AddModelError(Constants.FileError, Constants.NotFoundCode);
            return this.GenBadRequestProblem();
        }

        if(user.MayEdit(parentFolder) && user.MayEdit(childFolder))
        {
            childFolder.MasterFolder = parentFolder;
            await _db.SaveChangesAsync();
        }
        else
        {
            ModelState.AddModelError(Constants.FileError, Constants.CannotEditCode);
            return this.GenBadRequestProblem();
        }

        var folders = user.Folders;

        return Ok(folders);
    }
    
    [Route("AddSubFile")]
    [HttpPost]
    
    public async Task<ActionResult> AddSubFile(string FolderId, string FileId)
    {
        var userProto = await _userManager.GetUserAsync(User);
        var user = await _db.Users
            .Where(u => u.Id == userProto.Id)
            .Include(u => u.Folders)
            .ThenInclude(f => f.Files)
            .FirstOrDefaultAsync(u => u.Id == userProto.Id);

        if (user is null)
        {
            throw new Exception("User with a claimsprincipal not found"); 
        }
        
        var folder = await _db.Folders.FirstOrDefaultAsync(f => f.Id == FolderId);
        var file = await _db.Files.FirstOrDefaultAsync(f => f.ObjectId == FileId);
        if(folder is null || file is null)
        {
            ModelState.AddModelError(Constants.FileError, Constants.NotFoundCode);
            return this.GenBadRequestProblem();
        }
        
        if(file.ParentId == folder.Id || !folder.Files.TrueForAll(x => x.ObjectId != file.ObjectId))
        {
            ModelState.AddModelError(Constants.FileError, Constants.CannotAddIllegalCode);
            return this.GenBadRequestProblem();
        }
        
        if(user.MayEdit(folder) && user.MayEdit(file))
        {
            folder.Files.Add(file);
            file.ParentId = folder.Id;
            await _db.SaveChangesAsync();
        }
        else
        {
            ModelState.AddModelError(Constants.FileError, Constants.CannotEditCode);
            return this.GenBadRequestProblem();
        }

        var folders = user.Folders;
        
        return Ok(folders);
    }
    
    [Route("RemoveSubFolder")]
    [HttpDelete]
    
    public async Task<ActionResult> RemoveSubFolder(string ParentId, string ChildId)
    {
        var userProto = await _userManager.GetUserAsync(User);
        var user = await _db.Users
            .Where(u => u.Id == userProto.Id)
            .Include(u => u.Folders)
            .ThenInclude(f => f.Files)
            .Include(u => u.Folders)
            .FirstOrDefaultAsync(u => u.Id == userProto.Id);

        if (user is null)
        {
            throw new Exception("User with a claimsprincipal not found"); 
        }
        
        var parentFolder = await _db.Folders.FirstOrDefaultAsync(f => f.Id == ParentId);
        var childFolder = await _db.Folders.FirstOrDefaultAsync(f => f.Id == ChildId);
        if(parentFolder is null || childFolder is null)
        {
            ModelState.AddModelError(Constants.FileError, Constants.NotFoundCode);
            return this.GenBadRequestProblem();
        }
        
        if(childFolder.MasterFolderId != parentFolder.Id)
        {
            ModelState.AddModelError(Constants.FileError, Constants.CannotDeleteIllegalCode);
            return this.GenBadRequestProblem();
        }
        
        if(user.MayEdit(parentFolder) && user.MayEdit(childFolder))
        {
            childFolder.MasterFolder = null;
            await _db.SaveChangesAsync();
        }
        else
        {
            ModelState.AddModelError(Constants.FileError, Constants.CannotEditCode);
            return this.GenBadRequestProblem();
        }

        return Ok(user.Folders);
    }
    
    [Route("RemoveSubFile")]
    [HttpDelete]
    
    public async Task<ActionResult> RemoveSubFile(string FolderId, string FileId)
    {
        var userProto = await _userManager.GetUserAsync(User);
        var user = await _db.Users
            .Where(u => u.Id == userProto.Id)
            .Include(u => u.Folders)
            .ThenInclude(f => f.Files)
            .FirstOrDefaultAsync(u => u.Id == userProto.Id);

        if (user is null)
        {
            throw new Exception("User with a claimsprincipal not found"); 
        }
        
        var folder = await _db.Folders.FirstOrDefaultAsync(f => f.Id == FolderId);
        var file = await _db.Files.FirstOrDefaultAsync(f => f.ObjectId == FileId);
        if(folder is null || file is null)
        {
            ModelState.AddModelError(Constants.FileError, Constants.NotFoundCode);
            return this.GenBadRequestProblem();
        }
        
        if(folder.Files.TrueForAll(x => x.ObjectId != file.ObjectId))
        {
            ModelState.AddModelError(Constants.FileError, Constants.CannotDeleteIllegalCode);
            return this.GenBadRequestProblem();
        }
        
        if(user.MayEdit(folder) && user.MayEdit(file))
        {
            folder.Files.Remove(file);
            if (file.ParentId == folder.Id)
            {
                file.ParentId = null;
            }
            await _db.SaveChangesAsync();
        }
        else
        {
            ModelState.AddModelError(Constants.FileError, Constants.CannotEditCode);
            return this.GenBadRequestProblem();
        }

        return Ok(user.Folders);
    }
    
    [Route("DeleteFolder")]
    [HttpDelete]
    
    public async Task<ActionResult> RemoveFolder(string FolderId)
    {
        var userProto = await _userManager.GetUserAsync(User);
        var user = await _db.Users
            .Where(u => u.Id == userProto.Id)
            .Include(u => u.Folders)
            .ThenInclude(f => f.Files)
            .FirstOrDefaultAsync(u => u.Id == userProto.Id);

        if (user is null)
        {
            throw new Exception("User with a claimsprincipal not found"); 
        }
        
        var folder = await _db.Folders.FirstOrDefaultAsync(f => f.Id == FolderId);
        if(folder is null)
        {
            ModelState.AddModelError(Constants.FileError, Constants.NotFoundCode);
            return this.GenBadRequestProblem();
        }
        
        if(user.MayEdit(folder))
        {
            _db.Folders.Remove(folder);
            await _db.SaveChangesAsync();
        }
        else
        {
            ModelState.AddModelError(Constants.FileError, Constants.CannotEditCode);
            return this.GenBadRequestProblem();
        }

        return Ok(user.Folders);
    }

    [Route("DeleteFile")]
    [HttpDelete]
    public async Task<ActionResult> DeleteFile(string fileId)
    {
        var user = await _userManager.GetUserAsync(User);
        try
        {
            await user.DeleteFile(fileId, _serviceProvider);
        }
        catch (Exception e)
        {
            ModelState.AddModelError(Constants.FileError, e.Message);
            return this.GenBadRequestProblem();
        }
        

        return Ok(fileId);
    }
    
    [HttpPost]
    [Route("UploadFile")]
    [DisableRequestSizeLimit] 
    [ProducesResponseType(typeof(string), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(500)]
    public async Task<IActionResult> OnPost(IFormFile file, bool isPublic)
    {
        if (file is null)
        {
            ModelState.AddModelError(Constants.FileError, "No file");
            return this.GenBadRequestProblem();
        }

        try
        {
            ApplicationUser user = await _userManager.GetUserAsync(User);
            string id = await user.UploadFile(file, _serviceProvider, _minioConfig.BucketName, isPublic);
            return Ok(id);
        }
        catch(Exception error)
        {
            Console.WriteLine(error);
            return StatusCode((int) HttpStatusCode.InternalServerError);
        }
        
    }
    
    [HttpGet]
    [Route("GetFile")]
    [ProducesResponseType(typeof(string), 200)]
    [ProducesResponseType(500)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> GetFile(string id)
    {
        ApplicationUser user = await _userManager.GetUserAsync(User);
        File file = await _db.Files.Where(file => file.ObjectId == id).FirstOrDefaultAsync();
        if (file == default(File))
        {
            ModelState.AddModelError(Constants.FileError, Constants.NotFoundCode);
            return this.GenNotFound();
        }
        if (!file.MayView(user))
        {
            return Unauthorized();
        }

        if (!file.BackedInMinio)
        {
            ModelState.AddModelError(Constants.FileError, Constants.FileNotBacked);
            return this.GenBadRequestProblem();
        }

        return Ok(file.GetSignedUrl(_minioClient));
    }
    
    [HttpGet]
    [Route("GetFilesInfo")]
    [ProducesResponseType(typeof(File), 200)]
    [ProducesResponseType(500)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> GetFilesInfo(string id)
    {
        ApplicationUser user = await _userManager.GetUserAsync(User);
        File file = await _db.Files.Where(file => file.ObjectId == id).FirstOrDefaultAsync();
        if (file == default(File))
        {
            ModelState.AddModelError(Constants.FileError, Constants.NotFoundCode);
            return this.GenNotFound();
        }
        if (!file.MayView(user))
        {
            return Unauthorized();
        }

        if (!file.BackedInMinio)
        {
            ModelState.AddModelError(Constants.FileError, Constants.FileNotBacked);
            return this.GenBadRequestProblem();
        }

        return Ok(file);
    }
    
    [HttpGet]
    [Route("GetMyArticles")]
    [ProducesResponseType(typeof(AccountsData.Models.DataModels.Article[]), 200)]
    [ProducesResponseType(500)]
    public async Task<IActionResult> GeMyArticles()
    {
        ApplicationUser userProto = await _userManager.GetUserAsync(User);
        ApplicationUser user = await _db.Users
            .Where(u => u.Id == userProto.Id)
            .Include(u => u.Articles)
            .FirstOrDefaultAsync(u => u.Id == userProto.Id);
        return Ok(user.Articles);
    }
}