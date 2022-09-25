using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace KCert.Filters;

public class LocalPortFilterAttribute : ActionFilterAttribute
{
    private readonly int _port;

    public LocalPortFilterAttribute(int port)
    {
        _port = port;
    }

    public override void OnActionExecuted(ActionExecutedContext context)
    {
        if (context.HttpContext.Connection.LocalPort != _port)
        {
            context.Result = new NotFoundResult();
        }
    }

    public override void OnActionExecuting(ActionExecutingContext context)
    {
    }
}
