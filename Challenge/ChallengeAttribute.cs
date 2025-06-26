namespace KCert.Challenge;

[AttributeUsage(AttributeTargets.Class)]
public class ChallengeAttribute(string type) : Attribute
{
    public string ChallengeType { get; } = type;
}
