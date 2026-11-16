namespace RimMind.ModelService.Models
{
    public enum ProviderType
    {
        OpenAICompatible = 0,
        AnthropicClaude = 1,
        LocalSubscriptionGateway = 2,
        OpenCodeGo = 3,
        DeepSeekOfficial = 4
    }

    public enum BalancingStrategy
    {
        PriorityFailover = 0,
        RoundRobin = 1
    }

    public enum EndpointThinkingPolicy
    {
        FollowRequest = 0,
        ForceEnabled = 1,
        ForceDisabled = 2
    }

    public enum EndpointSafetyLevel
    {
        Standard = 0,
        High = 1
    }
}
