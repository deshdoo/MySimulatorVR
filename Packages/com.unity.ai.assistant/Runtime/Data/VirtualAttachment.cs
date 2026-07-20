using Unity.AI.Assistant.Data;

namespace Unity.AI.Assistant
{
    class VirtualAttachment
    {
        public VirtualAttachment(string payload, string type, string displayName, object metadata)
        {
            Payload = payload;
            Type = type;
            DisplayName = displayName;
            Metadata =  metadata;
        }

        public readonly string Payload;
        public string Type;
        public string DisplayName;
        public object Metadata;

        public override bool Equals(object obj)
        {
            if (obj is not VirtualAttachment other)
            {
                return false;
            }

            // Compare by Payload and Type since Payload contains the unique PNG data
            return Payload == other.Payload && Type == other.Type;
        }

        public override int GetHashCode()
        {
            // Use Payload and Type for hash code generation
            return System.HashCode.Combine(Payload, Type);
        }
    }
}
