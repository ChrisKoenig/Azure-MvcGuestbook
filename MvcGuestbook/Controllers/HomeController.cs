using MvcGuestbook_Data;
using System.Web;
using System.Web.Mvc;

namespace MvcGuestbook.Controllers
{
    public class HomeController : Controller
    {
        public ActionResult Index()
        {
            return View();
        }

        [HttpPost]
        public ActionResult Index(string username, string message, HttpPostedFileBase inputFile)
        {
            // add the guestbook entry to Azure
            var azure = new GuestBookService("DataConnectionString");
            azure.AddGuestBookEntry(username, message, inputFile.FileName, inputFile.ContentType, inputFile.InputStream);

            // redirect back to the "GET" action
            return RedirectToAction("Index");
        }

        [NoCache]
        public JsonResult Entries()
        {
            // pull out the guestbook entries
            var azure = new GuestBookService("DataConnectionString");
            var entries = azure.GetGuestBookEntries();

            // return the entries as JSON
            return Json(entries, JsonRequestBehavior.AllowGet);
        }
    }

    public class NoCacheAttribute : ActionFilterAttribute
    {
        public override void OnActionExecuted(ActionExecutedContext context)
        {
            context.HttpContext.Response.Cache.SetCacheability(HttpCacheability.NoCache);
        }
    }
}