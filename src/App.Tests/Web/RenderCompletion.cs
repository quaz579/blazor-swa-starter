using Bunit;

namespace App.Tests.Web;

internal static class RenderCompletion
{
    public static async Task AfterAsync(IRenderedFragment cut, Func<bool> ready, Func<Task> action)
    {
        var rendered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnRender(object? sender, EventArgs args)
        {
            if (ready())
            {
                rendered.TrySetResult();
            }
        }
        cut.OnAfterRender += OnRender;
        try
        {
            await action();
            await cut.InvokeAsync(() => OnRender(null, EventArgs.Empty));
            await rendered.Task;
        }
        finally
        {
            cut.OnAfterRender -= OnRender;
        }
    }
}
