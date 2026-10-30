namespace PostmanClone.EnvironmentsService.Environments;

/// <summary>The sizes and counts CONTRACTS §3 fixes. Attributes read them, so they are constants.</summary>
public static class EnvironmentLimits
{
    public const int NameMaxLength = 100;
    public const int KeyMaxLength = 100;
    public const int VariableValueMaxLength = 8192;
    public const int VariablesPerEnvironment = 200;
    public const int EnvironmentsPerWorkspace = 50;
}
