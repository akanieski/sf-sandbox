using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;

namespace StatelessWebApp.Pages
{
    public class IndexModel : PageModel
    {
        private readonly ILogger<IndexModel> _logger;

        public IndexModel(ILogger<IndexModel> logger)
        {
            _logger = logger;
        }

        public void OnGet()
        {
            ViewData["ProcessID"] = System.Diagnostics.Process.GetCurrentProcess().Id;
            ViewData["Title"] = $"Home Page - {System.Diagnostics.Process.GetCurrentProcess().Id}";
            ViewData["WEBROOT"] = Environment.GetEnvironmentVariable("ASPNETCORE_WEBROOT");
        }
    }
}
