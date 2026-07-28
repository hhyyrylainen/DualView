using DualView.Shared.Responses;
using Microsoft.AspNetCore.Mvc;

namespace DualView.Server.Controllers;

[ApiController]
[Route("api/v1/ping")]
public class PingController : Controller
{
    public ActionResult<PingResponse> Index()
    {
        return new PingResponse { Message = "pong" };
    }
}
