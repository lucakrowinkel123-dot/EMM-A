using System;
using System.Globalization;

namespace EMMA3.Client
{
    internal sealed class RememberedEntityReference<T>
        where T : class
    {
        private readonly string bridgeKey;
        private readonly Func<long, T> resolver;
        private readonly Func<T, bool> validator;
        private long entityId;
        private T current;

        public RememberedEntityReference(string bridgeKey, Func<long, T> resolver, Func<T, bool> validator)
        {
            if (string.IsNullOrWhiteSpace(bridgeKey))
                throw new ArgumentException("Bridge key is required.", nameof(bridgeKey));
            this.bridgeKey = bridgeKey;
            this.resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
            this.validator = validator ?? throw new ArgumentNullException(nameof(validator));
        }

        public long EntityId => entityId;

        public void Observe(T value, long observedEntityId)
        {
            if (value == null || observedEntityId <= 0 || !validator(value))
                return;
            current = value;
            entityId = observedEntityId;
        }

        public void Load(BridgeDocument bridge)
        {
            if (bridge == null)
                return;
            long saved;
            if (!long.TryParse(bridge.Get(bridgeKey), NumberStyles.Integer, CultureInfo.InvariantCulture, out saved) || saved <= 0)
                return;
            if (entityId != saved)
                current = null;
            entityId = saved;
        }

        public void Publish(BridgeDocument bridge)
        {
            if (bridge != null && entityId > 0)
                bridge.Set(bridgeKey, entityId);
        }

        public T Resolve()
        {
            if (current != null && validator(current))
                return current;
            current = null;
            if (entityId <= 0)
                return null;
            T candidate = resolver(entityId);
            if (candidate != null && validator(candidate))
                current = candidate;
            return current;
        }
    }
}
