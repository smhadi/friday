namespace Friday.Abstractions;

public interface IRequest<out TResponse>
{ } 

public record Request<TResponse> : IRequest<TResponse>
{ 
    // [JsonIgnore]
    // public string LoggedInUserId { get; set; } = string.Empty;
    //
    // [JsonIgnore]
    // public string LoggedInUsername { get; set; } = string.Empty;
}
