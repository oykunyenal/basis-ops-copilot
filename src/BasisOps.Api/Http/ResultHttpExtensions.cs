using BasisOps.Application.Abstractions;

namespace BasisOps.Api.Http;

/// <summary>Maps application results to HTTP: Validation 400, NotFound 404, Conflict 409, Forbidden 403.</summary>
internal static class ResultHttpExtensions
{
    public static IResult ToHttpResult<T>(this Result<T> result, Func<T, IResult>? onSuccess = null) =>
        result.IsSuccess
            ? onSuccess?.Invoke(result.Value) ?? TypedResults.Ok(result.Value)
            : result.Error!.ToProblem();

    public static IResult ToProblem(this Error error)
    {
        var extensions = new Dictionary<string, object?> { ["code"] = error.Code };

        return error.Kind switch
        {
            ErrorKind.Validation => TypedResults.ValidationProblem(
                error.ValidationErrors ?? new Dictionary<string, string[]>(),
                detail: error.Message,
                extensions: extensions),
            ErrorKind.NotFound => Problem(StatusCodes.Status404NotFound, "Not found"),
            ErrorKind.Conflict => Problem(StatusCodes.Status409Conflict, "Conflict"),
            ErrorKind.Forbidden => Problem(StatusCodes.Status403Forbidden, "Forbidden"),
            _ => throw new ArgumentOutOfRangeException(nameof(error), error.Kind, "Unknown error kind."),
        };

        IResult Problem(int status, string title) =>
            TypedResults.Problem(detail: error.Message, statusCode: status, title: title, extensions: extensions);
    }
}
