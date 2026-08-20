using McServerMgmnt.Data;
using Newtonsoft.Json;

namespace McServerMgmnt.Services.Factories;

public class ServerWhitelistStore : IServerWhitelist
{
    private const string WHITELIST_FILE = "whitelist.json";
    
    public bool IsConnected { get; private set; }

    private static List<Player> _whiteListedPlayers = [];
    private static readonly HttpClient Client = new();

    public async Task<IReadOnlyList<Player>> GetWhitelistAsync(CancellationToken ct = default)
    {
        IsConnected = File.Exists(WHITELIST_FILE);

        if (!IsConnected)
            return [];
        
        var fileContent = await File.ReadAllTextAsync(WHITELIST_FILE, ct);
        _whiteListedPlayers = JsonConvert.DeserializeObject<List<Player>>(fileContent)!;
        
        return _whiteListedPlayers;
    }

    public async Task SaveAsync(Player player, CancellationToken ct = default)
    {
        _whiteListedPlayers.Add(player);
        await File.WriteAllTextAsync(WHITELIST_FILE, JsonConvert.SerializeObject(_whiteListedPlayers, Formatting.Indented), ct);
    }

    public async Task RemoveAsync(string playerName, CancellationToken ct = default)
    {
        var player = await GetPlayer(playerName, ct);
        _whiteListedPlayers.Remove(player);
        
        await File.WriteAllTextAsync(WHITELIST_FILE, JsonConvert.SerializeObject(_whiteListedPlayers, Formatting.Indented), ct);
    }

    public async Task<Player> GetPlayer(string playerName, CancellationToken ct = default)
    {
        var url = "https://api.mojang.com/minecraft/profile/lookup/name/" + playerName;
        var response = await Client.GetStringAsync(url, ct);
        return JsonConvert.DeserializeObject<MojangPlayer>(response)!.ToPlayer();
    }
}