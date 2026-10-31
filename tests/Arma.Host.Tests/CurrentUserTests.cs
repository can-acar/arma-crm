using System.Security.Claims;
using Arma.Host.Identity;

namespace Arma.Host.Tests;

public class CurrentUserTests
{
    [Fact]
    public void Reads_user_id_from_name_identifier_claim()
    {
        var id = Guid.CreateVersion7();
        var currentUser = new CurrentUser();

        currentUser.Set(new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, id.ToString())], "test")));

        currentUser.UserId.ShouldBe(id);
    }

    [Fact]
    public void Anonymous_user_has_no_id() => new CurrentUser().UserId.ShouldBeNull();
}
