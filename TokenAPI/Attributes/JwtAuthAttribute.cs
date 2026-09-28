using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace TokenAPI.Attributes
{
    public class JwtAuthAttribute : ActionFilterAttribute
    {
        public override void OnActionExecuting(ActionExecutingContext context)
        {
            //TODO: Implement proper authentication and authorization checks

            return;
        }
    }
}
