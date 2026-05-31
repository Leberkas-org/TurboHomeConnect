namespace TurboHomeConnect;

/// <summary>Well-known Home Connect endpoints. Production hits real appliances; the simulator
/// is for development and lives at a separate host.</summary>
public static class HomeConnectEndpoints
{
    public static readonly Uri Production = new("https://api.home-connect.com/");
    public static readonly Uri Simulator = new("https://simulator.home-connect.com/");

    public static readonly Uri ProductionAuthorizeEndpoint = new("https://api.home-connect.com/security/oauth/authorize");
    public static readonly Uri ProductionTokenEndpoint = new("https://api.home-connect.com/security/oauth/token");

    public static readonly Uri SimulatorAuthorizeEndpoint = new("https://simulator.home-connect.com/security/oauth/authorize");
    public static readonly Uri SimulatorTokenEndpoint = new("https://simulator.home-connect.com/security/oauth/token");
}
