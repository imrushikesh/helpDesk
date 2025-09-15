using Microsoft.OpenApi.Models;
using helpdesk.Api.Services;

var builder = WebApplication.CreateBuilder(args);

// Add configuration and services
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "Helpdesk API", Version = "v1" });
});

// Allow local frontend (vite) if you use it; adjust origin if needed
builder.Services.AddCors(options =>
{
    options.AddPolicy("local", p => p
        .WithOrigins("http://localhost:5173", "http://localhost:3000")
        .AllowAnyHeader()
        .AllowAnyMethod());
});

// Http client factory and singleton service registration
builder.Services.AddHttpClient();
builder.Services.AddSingleton<HuggingFaceClient>();
builder.Services.AddSingleton<PineconeClient>();

var app = builder.Build();

 
  
    app.UseSwagger();
    app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "Helpdesk API v1"));
 
 
app.UseCors("local");
app.UseHttpsRedirection();
app.MapControllers();

app.Run();
