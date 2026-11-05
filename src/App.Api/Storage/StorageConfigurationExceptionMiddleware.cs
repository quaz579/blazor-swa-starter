using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Middleware;
using Microsoft.AspNetCore.Http;

namespace App.Api.Storage;

internal sealed class StorageConfigurationExceptionMiddleware : IFunctionsWorkerMiddleware
{
    private readonly Func<FunctionContext, HttpContext?> _getHttpContext;

    public StorageConfigurationExceptionMiddleware()
        : this(context => context.GetHttpContext())
    {
    }

    internal StorageConfigurationExceptionMiddleware(Func<FunctionContext, HttpContext?> getHttpContext)
    {
        _getHttpContext = getHttpContext;
    }

    public async Task Invoke(FunctionContext context, FunctionExecutionDelegate next)
    {
        try
        {
            await next(context);
        }
        catch (StorageConfigurationException exception)
        {
            var response = _getHttpContext(context)?.Response;
            if (response is null || response.HasStarted)
                throw;

            response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            await response.WriteAsJsonAsync(new { error = exception.Message, storageMode = exception.StorageMode });
        }
    }
}
