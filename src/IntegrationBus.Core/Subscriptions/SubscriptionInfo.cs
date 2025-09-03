namespace IntegrationBus.Core.Subscriptions;

public class SubscriptionInfo
{
    public Type HandlerType { get; private set; }
    public bool IsDynamic { get; private set; }

    private SubscriptionInfo(bool isDynamic, Type handlerType)
    {
        IsDynamic = isDynamic;
        HandlerType = handlerType;
    }

    public static SubscriptionInfo Typed(Type handlerType) => new(false, handlerType);
    public static SubscriptionInfo Dynamic(Type handlerType) => new(true, handlerType);
}