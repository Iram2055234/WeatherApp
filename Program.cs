using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.AspNetCore.Http;
using System.Text.Json.Serialization;
using System.Text.Json;
using System.IO;
using System;

var builder = WebApplication.CreateBuilder(args);

// Configuración de sesiones y servicios
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromHours(2);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});
builder.Services.AddHttpClient();
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();
    });
});

// In-memory storage para mensajes de contacto
var mensajes = new List<ContactMessage>();

var app = builder.Build();

app.UseStaticFiles();
app.UseRouting();
app.UseCors();
app.UseSession();

// Redirigir raíz a index.html
app.MapGet("/", (HttpContext http) =>
{
    http.Response.Redirect("/index.html");
    return Results.Ok();
});

// Endpoint para login
app.MapPost("/api/login", async (HttpContext http) =>
{
    try
    {
        using var sr = new StreamReader(http.Request.Body);
        var bodyText = await sr.ReadToEndAsync();
        if (string.IsNullOrWhiteSpace(bodyText))
        {
            http.Response.StatusCode = 400;
            await http.Response.WriteAsJsonAsync(new { message = "Email y contraseña requeridos" });
            return;
        }

        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var body = JsonSerializer.Deserialize<LoginRequest>(bodyText, options);

        if (body is null || string.IsNullOrWhiteSpace(body.Email) || string.IsNullOrWhiteSpace(body.Password))
        {
            http.Response.StatusCode = 400;
            await http.Response.WriteAsJsonAsync(new { message = "Email y contraseña requeridos" });
            return;
        }

        // Credenciales hardcodeadas
        if (body.Email.Equals("admin@weatherwise.com", StringComparison.OrdinalIgnoreCase)
            && body.Password == "Admin123")
        {
            http.Session.SetString("user", body.Email);
            await http.Response.WriteAsJsonAsync(new { success = true, email = body.Email });
            return;
        }

        http.Response.StatusCode = 401;
        await http.Response.WriteAsJsonAsync(new { message = "Credenciales inválidas" });
    }
    catch (Exception ex)
    {
        http.Response.StatusCode = 500;
        await http.Response.WriteAsJsonAsync(new { message = "Error interno", error = ex.Message });
    }
});

// Endpoint logout
app.MapPost("/api/logout", (HttpContext http) =>
{
    http.Session.Remove("user");
    return Results.Ok(new { success = true });
});

// Verificar sesión
app.MapGet("/api/verificar-sesion", (HttpContext http) =>
{
    var user = http.Session.GetString("user");
    if (string.IsNullOrEmpty(user))
        return Results.Ok(new { authenticated = false });
    return Results.Ok(new { authenticated = true, email = user });
});

// Endpoint contacto
app.MapPost("/api/contacto", async (HttpContext http) =>
{
    var contact = await JsonSerializer.DeserializeAsync<ContactMessage>(http.Request.Body);
    if (contact is null || string.IsNullOrWhiteSpace(contact.Nombre) ||
        string.IsNullOrWhiteSpace(contact.Email) || string.IsNullOrWhiteSpace(contact.Mensaje) ||
        contact.Mensaje.Length < 10)
    {
        http.Response.StatusCode = 400;
        await http.Response.WriteAsJsonAsync(new { message = "Datos inválidos. Revisa los campos requeridos." });
        return;
    }

    contact.Id = Guid.NewGuid().ToString();
    contact.Fecha = DateTime.UtcNow;
    mensajes.Add(contact);

    await http.Response.WriteAsJsonAsync(new { success = true, id = contact.Id });
});

// Endpoint clima que consume OpenWeatherMap
app.MapGet("/api/clima", async (HttpContext http, IHttpClientFactory clientFactory) =>
{
    var ciudad = http.Request.Query["ciudad"].ToString();
    if (string.IsNullOrWhiteSpace(ciudad))
    {
        http.Response.StatusCode = 400;
        await http.Response.WriteAsJsonAsync(new { message = "Parámetro 'ciudad' requerido" });
        return;
    }

    var config = app.Configuration;
    var apiKey = config["OpenWeather:ApiKey"]
                 ?? Environment.GetEnvironmentVariable("OpenWeather__ApiKey")
                 ?? string.Empty;

    if (string.IsNullOrWhiteSpace(apiKey))
    {
        http.Response.StatusCode = 500;
        await http.Response.WriteAsJsonAsync(new { message = "API Key de OpenWeather no configurada" });
        return;
    }

    var client = clientFactory.CreateClient();
    var url = $"https://api.openweathermap.org/data/2.5/weather?q={Uri.EscapeDataString(ciudad)}&appid={apiKey}&units=metric&lang=es";

    try
    {
        var resp = await client.GetAsync(url);
        var text = await resp.Content.ReadAsStringAsync();

        if (!resp.IsSuccessStatusCode)
        {
            http.Response.StatusCode = (int)resp.StatusCode;
            await http.Response.WriteAsJsonAsync(new { message = "Error desde OpenWeather", status = resp.StatusCode, details = text });
            return;
        }

        using var doc = JsonDocument.Parse(text);
        var root = doc.RootElement;

        string nombre = root.GetProperty("name").GetString() ?? ciudad;
        string pais = root.GetProperty("sys").GetProperty("country").GetString() ?? "";
        double temp = root.GetProperty("main").GetProperty("temp").GetDouble();
        double feels = root.GetProperty("main").GetProperty("feels_like").GetDouble();
        string descripcion = root.GetProperty("weather")[0].GetProperty("description").GetString() ?? "";
        string icon = root.GetProperty("weather")[0].GetProperty("icon").GetString() ?? "";
        int humedad = root.GetProperty("main").GetProperty("humidity").GetInt32();
        double viento = root.GetProperty("wind").GetProperty("speed").GetDouble();
        int presion = root.GetProperty("main").GetProperty("pressure").GetInt32();

        var result = new
        {
            ciudad = nombre,
            pais = pais,
            temperatura = Math.Round((decimal)temp, 1),
            sensacion = Math.Round((decimal)feels, 1),
            descripcion = descripcion,
            icon = icon,
            humedad = humedad,
            viento = Math.Round((decimal)viento, 1),
            presion = presion
        };

        await http.Response.WriteAsJsonAsync(result);
    }
    catch (JsonException jex)
    {
        http.Response.StatusCode = 500;
        await http.Response.WriteAsJsonAsync(new { message = "Error al parsear respuesta de OpenWeather", error = jex.Message });
    }
    catch (Exception ex)
    {
        http.Response.StatusCode = 500;
        await http.Response.WriteAsJsonAsync(new { message = "Error interno al obtener clima", error = ex.Message });
    }
});

app.Run();

// Modelos
record LoginRequest(string Email, string Password);
record ContactMessage
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }
    [JsonPropertyName("nombre")]
    public string? Nombre { get; set; }
    [JsonPropertyName("email")]
    public string? Email { get; set; }
    [JsonPropertyName("ciudad")]
    public string? Ciudad { get; set; }
    [JsonPropertyName("mensaje")]
    public string? Mensaje { get; set; }
    [JsonPropertyName("newsletter")]
    public bool Newsletter { get; set; }
    [JsonPropertyName("fecha")]
    public DateTime Fecha { get; set; }
}
