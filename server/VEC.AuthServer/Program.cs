using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using VEC.AuthServer.Models;
using VEC.AuthServer.Services;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenAnyIP(8080);
});

builder.Services.AddSingleton<DatabaseService>();
builder.Services.AddSingleton<YggdrasilService>();
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();
    });
});

var app = builder.Build();
app.UseCors();

Directory.CreateDirectory("data/skins");
Directory.CreateDirectory("data/capes");

app.MapGet("/api/info", () => Results.Json(new
{
    service = "VEC Auth Server",
    organization = "Vyacheslav's Engineering Company (VEC)",
    platform = "Kostanay Polytechnic Higher College",
    status = "running",
    version = "1.0.0",
    docs = "https://kpvk.edu.kz/"
}));

app.MapGet("/api/status", () => Results.Ok(new { status = "online", timestamp = DateTimeOffset.UtcNow }));

app.MapPost("/api/auth/register", (RegisterRequest req, DatabaseService db, YggdrasilService ygg, HttpContext ctx) =>
{
    var cleanUser = req.Username?.Trim() ?? "";
    if (cleanUser.Length < 3 || cleanUser.Length > 16)
        return Results.BadRequest(new { error = "Username must be between 3 and 16 characters." });

    if (string.IsNullOrEmpty(req.Password) || req.Password.Length < 4)
        return Results.BadRequest(new { error = "Password must be at least 4 characters." });

    var existing = db.GetUserByUsername(cleanUser);
    if (existing != null)
        return Results.Conflict(new { error = $"User '{cleanUser}' is already registered." });

    var user = db.CreateUser(cleanUser, req.Password, req.Email);
    var session = db.CreateSession(user.Id);

    var baseUrl = $"{ctx.Request.Scheme}://{ctx.Request.Host}";
    var skinUrl = $"{baseUrl}/api/skin/{user.Username}.png";

    return Results.Ok(new AuthResponse(
        session.AccessToken,
        session.ClientToken,
        new ProfileDto(user.Id, user.Username),
        new UserDto(user.Id, user.Username),
        skinUrl
    ));
});

app.MapPost("/api/auth/login", (LoginRequest req, DatabaseService db, YggdrasilService ygg, HttpContext ctx) =>
{
    var cleanUser = req.Username?.Trim() ?? "";
    var user = db.GetUserByUsername(cleanUser);
    if (user == null)
        return Results.NotFound(new { error = $"User '{cleanUser}' not found." });

    if (!DatabaseService.VerifyPassword(req.Password, user.PasswordSalt, user.PasswordHash))
        return Results.Unauthorized();

    db.UpdateLastLogin(user.Id);
    var session = db.CreateSession(user.Id, req.ClientToken);

    var baseUrl = $"{ctx.Request.Scheme}://{ctx.Request.Host}";
    var skinUrl = $"{baseUrl}/api/skin/{user.Username}.png";

    return Results.Ok(new AuthResponse(
        session.AccessToken,
        session.ClientToken,
        new ProfileDto(user.Id, user.Username),
        new UserDto(user.Id, user.Username),
        skinUrl
    ));
});

app.MapGet("/api/skin/{username}.png", (string username) =>
{
    var cleanName = Path.GetFileNameWithoutExtension(username);
    var skinPath = Path.Combine("data", "skins", $"{cleanName}.png");
    if (!File.Exists(skinPath))
    {
        var appDataSkin = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VECLauncher", "skins", $"{cleanName}.png");
        if (File.Exists(appDataSkin))
        {
            try
            {
                Directory.CreateDirectory("data/skins");
                File.Copy(appDataSkin, skinPath, overwrite: true);
                return Results.File(Path.GetFullPath(skinPath), "image/png");
            }
            catch { }
        }
        return Results.NotFound();
    }
    return Results.File(Path.GetFullPath(skinPath), "image/png");
});

app.MapGet("/api/cape/{username}.png", (string username) =>
{
    var cleanName = Path.GetFileNameWithoutExtension(username);
    var capePath = Path.Combine("data", "capes", $"{cleanName}.png");
    if (!File.Exists(capePath))
    {
        var appDataCape = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VECLauncher", "capes", $"{cleanName}.png");
        if (File.Exists(appDataCape))
        {
            try
            {
                Directory.CreateDirectory("data/capes");
                File.Copy(appDataCape, capePath, overwrite: true);
                return Results.File(Path.GetFullPath(capePath), "image/png");
            }
            catch { }
        }
        return Results.NotFound();
    }
    return Results.File(Path.GetFullPath(capePath), "image/png");
});

app.MapGet("/api/csl/{username}.json", (string username, DatabaseService db, HttpContext ctx) =>
{
    var cleanName = Path.GetFileNameWithoutExtension(username);
    var user = db.GetUserByUsername(cleanName) ?? db.GetUserById(cleanName) ?? db.GetOrCreateUser(cleanName);

    var isSlim = user.SkinModel.Equals("slim", StringComparison.OrdinalIgnoreCase);

    var skins = new Dictionary<string, string>();
    if (isSlim)
    {
        skins["slim"] = $"{user.Username}.png";
    }
    else
    {
        skins["default"] = $"{user.Username}.png";
    }

    var result = new Dictionary<string, object>
    {
        ["username"] = user.Username,
        ["skins"] = skins
    };

    var capePath = Path.Combine("data", "capes", $"{user.Username}.png");
    if (File.Exists(capePath))
    {
        result["cape"] = $"cape_{user.Username}.png";
    }

    return Results.Ok(result);
});

app.MapGet("/api/csl/textures/{filename}", (string filename) =>
{
    var cleanName = Path.GetFileNameWithoutExtension(filename);

    if (cleanName.StartsWith("cape_", StringComparison.OrdinalIgnoreCase))
    {
        var capeUser = cleanName.Substring(5);
        var capePath = Path.Combine("data", "capes", $"{capeUser}.png");
        if (File.Exists(capePath)) return Results.File(Path.GetFullPath(capePath), "image/png");

        var appDataCape = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VECLauncher", "capes", $"{capeUser}.png");
        if (File.Exists(appDataCape)) return Results.File(Path.GetFullPath(appDataCape), "image/png");
    }

    var skinPath = Path.Combine("data", "skins", $"{cleanName}.png");
    if (File.Exists(skinPath)) return Results.File(Path.GetFullPath(skinPath), "image/png");

    var appDataSkin = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VECLauncher", "skins", $"{cleanName}.png");
    if (File.Exists(appDataSkin)) return Results.File(Path.GetFullPath(appDataSkin), "image/png");

    return Results.NotFound();
});

app.MapPost("/api/skin/upload", async (HttpRequest request, DatabaseService db) =>
{
    if (!request.HasFormContentType)
        return Results.BadRequest(new { error = "Expected multipart/form-data." });

    var form = await request.ReadFormAsync();
    var username = form["username"].ToString();
    var uuid = form["uuid"].ToString();
    var model = form["model"].ToString();
    var file = form.Files.GetFile("file");

    if (string.IsNullOrEmpty(username) || file == null || file.Length == 0)
        return Results.BadRequest(new { error = "Missing file or username." });

    var user = db.GetUserByUsername(username) ?? db.GetOrCreateUser(username, uuid, model);

    Directory.CreateDirectory("data/skins");
    var skinPath = Path.Combine("data", "skins", $"{user.Username}.png");
    using (var fs = new FileStream(skinPath, FileMode.Create))
    {
        await file.CopyToAsync(fs);
    }

    if (!string.IsNullOrEmpty(model))
    {
        Console.WriteLine($"[skin/upload] Updating model for '{user.Username}': '{model}'");
        db.UpdateSkinModel(user.Username, model);
    }

    return Results.Ok(new { success = true, message = "Skin uploaded successfully." });
});

app.MapPost("/api/skin/reset", async (HttpRequest request, DatabaseService db) =>
{
    var form = await request.ReadFormAsync();
    var username = form["username"].ToString();
    var model = form["model"].ToString();

    if (string.IsNullOrEmpty(username))
        return Results.BadRequest(new { error = "Missing username." });

    var user = db.GetUserByUsername(username) ?? db.GetOrCreateUser(username);

    var skinPath = Path.Combine("data", "skins", $"{user.Username}.png");
    if (File.Exists(skinPath))
    {
        try { File.Delete(skinPath); } catch { }
    }

    var file = request.HasFormContentType ? (await request.ReadFormAsync()).Files.GetFile("file") : null;
    if (file != null && file.Length > 0)
    {
        Directory.CreateDirectory("data/skins");
        using var fs = new FileStream(skinPath, FileMode.Create);
        await file.CopyToAsync(fs);
    }

    if (!string.IsNullOrEmpty(model))
    {
        db.UpdateSkinModel(user.Username, model);
    }

    return Results.Ok(new { success = true, message = "Skin reset to default." });
});

app.MapPost("/api/skin/model", async (HttpRequest request, DatabaseService db) =>
{
    var form = await request.ReadFormAsync();
    var username = form["username"].ToString();
    var model = form["model"].ToString();

    if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(model))
        return Results.BadRequest(new { error = "Missing username or model." });

    var user = db.GetUserByUsername(username) ?? db.GetOrCreateUser(username);
    Console.WriteLine($"[skin/model] Updating model for '{user.Username}': '{model}'");
    db.UpdateSkinModel(user.Username, model);

    return Results.Ok(new { success = true, model = model });
});

app.MapPost("/api/cape/upload", async (HttpRequest request, DatabaseService db) =>
{
    if (!request.HasFormContentType)
        return Results.BadRequest(new { error = "Expected multipart/form-data." });

    var form = await request.ReadFormAsync();
    var username = form["username"].ToString();
    var file = form.Files.GetFile("file");

    if (string.IsNullOrEmpty(username) || file == null || file.Length == 0)
        return Results.BadRequest(new { error = "Missing file or username." });

    var user = db.GetUserByUsername(username);
    if (user == null) return Results.NotFound(new { error = "User not found." });

    var capePath = Path.Combine("data", "capes", $"{user.Username}.png");
    using (var fs = new FileStream(capePath, FileMode.Create))
    {
        await file.CopyToAsync(fs);
    }

    return Results.Ok(new { success = true, message = "Cape uploaded successfully." });
});

app.MapPost("/api/cape/reset", async (HttpRequest request, DatabaseService db) =>
{
    var form = await request.ReadFormAsync();
    var username = form["username"].ToString();
    if (string.IsNullOrEmpty(username)) return Results.BadRequest(new { error = "Missing username." });

    var cleanUser = Path.GetFileNameWithoutExtension(username);
    var capePath = Path.Combine("data", "capes", $"{cleanUser}.png");
    if (File.Exists(capePath))
    {
        try { File.Delete(capePath); } catch { }
    }

    return Results.Ok(new { success = true, message = "Cape removed." });
});

var promoCapes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

app.MapPost("/api/promo/create", async (HttpRequest request, DatabaseService db) =>
{
    var form = await request.ReadFormAsync();
    var username = form["username"].ToString();
    var code = form["code"].ToString();
    var capeUrl = form["capeUrl"].ToString();

    if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(code) || string.IsNullOrEmpty(capeUrl))
        return Results.BadRequest(new { error = "Missing username, code, or cape URL." });

    promoCapes[code] = capeUrl;
    return Results.Ok(new { success = true, message = $"Promo code '{code}' created." });
});

app.MapPost("/api/promo/redeem", async (HttpRequest request, DatabaseService db) =>
{
    var form = await request.ReadFormAsync();
    var username = form["username"].ToString();
    var code = form["code"].ToString();

    if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(code))
        return Results.BadRequest(new { error = "Missing username or promo code." });

    if (!promoCapes.TryGetValue(code, out var capeUrl))
        return Results.BadRequest(new { error = "Promo code not found or already used." });

    var user = db.GetUserByUsername(username) ?? db.GetOrCreateUser(username);

    try
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        var capeBytes = await http.GetByteArrayAsync(capeUrl);

        Directory.CreateDirectory("data/capes");
        var capePath = Path.Combine("data", "capes", $"{user.Username}.png");
        await File.WriteAllBytesAsync(capePath, capeBytes);
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { error = $"Failed to download cape: {ex.Message}" });
    }

    promoCapes.Remove(code);

    return Results.Ok(new { success = true, message = "Cape activated. Restart launcher to apply." });
});

app.MapGet("/api/promo/list", () =>
{
    return Results.Ok(new { codes = promoCapes.Keys.ToList() });
});

app.MapGet("/api/yggdrasil", (YggdrasilService ygg) => Results.Json(new
{
    meta = new
    {
        serverName = "VEC Auth Server",
        implementationName = "VEC.AuthServer",
        implementationVersion = "1.0.0"
    },
    skinDomains = app.Configuration.GetSection("Server:SkinDomains").Get<string[]>() ?? new[] { "localhost", "127.0.0.1" },
    signaturePublickey = ygg.GetPublicKeyPem()
}));

Func<JsonElement, DatabaseService, YggdrasilService, HttpContext, IResult> authHandler = (body, db, ygg, ctx) =>
{
    var username = body.GetProperty("username").GetString() ?? "";
    var password = body.GetProperty("password").GetString() ?? "";
    var clientToken = body.TryGetProperty("clientToken", out var ct) ? ct.GetString() : null;

    var user = db.GetUserByUsername(username);
    if (user == null || !DatabaseService.VerifyPassword(password, user.PasswordSalt, user.PasswordHash))
        return Results.Json(new { error = "ForbiddenOperationException", errorMessage = "Invalid credentials. Invalid username or password." }, statusCode: 403);

    db.UpdateLastLogin(user.Id);
    var session = db.CreateSession(user.Id, clientToken);

    var baseUrl = $"{ctx.Request.Scheme}://{ctx.Request.Host}";
    var skinUrl = $"{baseUrl}/api/skin/{user.Username}.png";

    return Results.Ok(new AuthResponse(
        session.AccessToken,
        session.ClientToken,
        new ProfileDto(user.Id, user.Username),
        new UserDto(user.Id, user.Username),
        skinUrl
    ));
};

app.MapPost("/authserver/authenticate", authHandler);
app.MapPost("/api/yggdrasil/authserver/authenticate", authHandler);

Func<JsonElement, DatabaseService, IResult> validateHandler = (body, db) =>
{
    var token = body.GetProperty("accessToken").GetString();
    if (string.IsNullOrEmpty(token)) return Results.Unauthorized();

    var session = db.GetSessionByToken(token);
    if (session == null || session.ExpiresAt < DateTimeOffset.UtcNow)
        return Results.Json(new { error = "ForbiddenOperationException", errorMessage = "Invalid token." }, statusCode: 403);

    return Results.NoContent();
};

app.MapPost("/authserver/validate", validateHandler);
app.MapPost("/api/yggdrasil/authserver/validate", validateHandler);

Func<JoinServerRequest, DatabaseService, IResult> joinHandler = (req, db) =>
{
    var session = db.GetSessionByToken(req.AccessToken);
    if (session == null)
        return Results.Json(new { error = "ForbiddenOperationException", errorMessage = "Invalid token." }, statusCode: 403);

    db.SetServerId(req.AccessToken, req.ServerId);
    return Results.NoContent();
};

app.MapPost("/sessionserver/session/minecraft/join", joinHandler);
app.MapPost("/api/yggdrasil/sessionserver/session/minecraft/join", joinHandler);

Func<string, string, DatabaseService, YggdrasilService, HttpContext, IResult> hasJoinedHandler = (username, serverId, db, ygg, ctx) =>
{
    var user = db.GetUserByServerId(username, serverId) ?? db.GetUserByUsername(username);
    if (user == null)
    {
        Console.WriteLine($"[hasJoined] User '{username}' not found, creating with default model classic");
        user = db.GetOrCreateUser(username);
    }
    else
    {
        Console.WriteLine($"[hasJoined] User '{user.Username}' found, skinModel='{user.SkinModel}'");
    }

    var baseUrl = $"{ctx.Request.Scheme}://{ctx.Request.Host}";
    var prop = ygg.BuildTexturesProperty(user, baseUrl);

    return Results.Ok(new HasJoinedResponse(
        user.Id,
        user.Username,
        new List<PropertyDto> { prop }
    ));
};

app.MapGet("/sessionserver/session/minecraft/hasJoined", hasJoinedHandler);
app.MapGet("/api/yggdrasil/sessionserver/session/minecraft/hasJoined", hasJoinedHandler);

Func<string, DatabaseService, YggdrasilService, HttpContext, IResult> profileHandler = (id, db, ygg, ctx) =>
{
    var cleanId = id.Replace("-", "");
    var user = db.GetUserById(cleanId) ?? db.GetUserByUsername(id);
    if (user == null)
    {
        Console.WriteLine($"[profile] User id='{id}' not found, creating with default model classic");
        user = db.GetOrCreateUser(id, cleanId);
    }
    else
    {
        Console.WriteLine($"[profile] User '{user.Username}' found, skinModel='{user.SkinModel}'");
    }

    var baseUrl = $"{ctx.Request.Scheme}://{ctx.Request.Host}";
    var prop = ygg.BuildTexturesProperty(user, baseUrl);

    return Results.Ok(new HasJoinedResponse(
        user.Id,
        user.Username,
        new List<PropertyDto> { prop }
    ));
};

app.MapGet("/sessionserver/session/minecraft/profile/{id}", profileHandler);
app.MapGet("/api/yggdrasil/sessionserver/session/minecraft/profile/{id}", profileHandler);

app.Run();
