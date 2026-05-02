using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace JSAPNEW.Filters
{
    public class SensitiveErrorResponseFilter : IResultFilter
    {
        public void OnResultExecuting(ResultExecutingContext context)
        {
            var statusCode = context.Result switch
            {
                ObjectResult objectResult => objectResult.StatusCode,
                ContentResult contentResult => contentResult.StatusCode,
                StatusCodeResult statusCodeResult => statusCodeResult.StatusCode,
                _ => null
            };

            if (statusCode >= StatusCodes.Status500InternalServerError)
            {
                context.Result = new ObjectResult(new
                {
                    success = false,
                    message = "Something went wrong"
                })
                {
                    StatusCode = StatusCodes.Status500InternalServerError
                };
            }
        }

        public void OnResultExecuted(ResultExecutedContext context)
        {
        }
    }
}
