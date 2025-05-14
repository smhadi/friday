using Friday.Abstractions;

namespace Infrastructure.FridayMediator.Samples;

public record PingRequest(string Message) : IRequest<IResult>;



public class PingHandler : IRequestHandler<PingRequest, IResult>
{
    public async Task<IResult> Handle(PingRequest request, CancellationToken cancellationToken)
    {
        await Task.CompletedTask;
        return Results.Ok($"Pong: {request.Message}");

        //commemnt by hadi
    }
}