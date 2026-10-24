namespace NightOwl.AI;

public sealed class GeminiOptions
{
    public string ApiKey { get; set; } = "";
    public string Model { get; set; } = "";
    public string Endpoint { get; set; } = "https://generativelanguage.googleapis.com/v1beta/openai/";
}