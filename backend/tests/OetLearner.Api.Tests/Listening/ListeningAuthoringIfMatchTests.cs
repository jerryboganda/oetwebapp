using Microsoft.AspNetCore.Http;
using OetLearner.Api.Domain;
using OetLearner.Api.Endpoints;

namespace OetLearner.Api.Tests.Listening;

public class ListeningAuthoringIfMatchTests
{
    private static IResult? Check(string? ifMatch)
    {
        var http = new DefaultHttpContext();
        if (ifMatch is not null) http.Request.Headers.IfMatch = ifMatch;
        return ListeningAuthoringAdminEndpoints.CheckIfMatch(http, new ContentPaper { RowVersion = 15 });
    }

    [Theory]
    [InlineData(null)]
    [InlineData("\"15\"")]
    [InlineData("W/\"15\"")] // what the gzip proxy hands back to clients
    [InlineData(" W/\"15\" ")]
    public void Matching_or_absent_version_proceeds(string? ifMatch) => Assert.Null(Check(ifMatch));

    [Theory]
    [InlineData("\"14\"")]
    [InlineData("W/\"14\"")]
    [InlineData("garbage")]
    public void Stale_or_invalid_version_is_412(string ifMatch)
    {
        var result = Assert.IsAssignableFrom<IStatusCodeHttpResult>(Check(ifMatch));
        Assert.Equal(StatusCodes.Status412PreconditionFailed, result.StatusCode);
    }
}
