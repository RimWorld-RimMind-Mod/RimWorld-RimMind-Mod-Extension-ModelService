namespace RimMind.ModelService.Models
{
    public enum ProviderType
    {
        OpenAICompatible = 0,
        AnthropicClaude = 1,
        LocalSubscriptionGateway = 2
    }

    public enum BalancingStrategy
    {
        PriorityFailover = 0,
        RoundRobin = 1
    }
}
