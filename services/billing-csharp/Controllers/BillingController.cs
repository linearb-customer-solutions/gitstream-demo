using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System;
using System.Collections.Generic;
using System.IO;

public class ChargeRequest
{
    [JsonPropertyName("username")]
    public string Username { get; set; }

    [JsonPropertyName("productId")]
    public string ProductId { get; set; }

    [JsonPropertyName("quantity")]
    public int Quantity { get; set; }

    // ISO-8601 timestamp of when the charge was requested
    [JsonPropertyName("date")]
    public DateTime Date { get; set; }
}

[ApiController]
[Route("billing")]
public class BillingController : ControllerBase
{
    private readonly string EXPECTED_SECRET = Environment.GetEnvironmentVariable("BILLING_SECRET");
    private static readonly string StorageDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "BillingData");

    [HttpPost("charge")]
    public async Task<IActionResult> Charge([FromBody] ChargeRequest request)
    {
        var receivedSecret = Request.Headers["X-Service-Secret"].ToString();
        if (string.IsNullOrEmpty(receivedSecret) || receivedSecret != EXPECTED_SECRET)
            return Unauthorized("Missing or invalid service secret");

        if (string.IsNullOrEmpty(request.Username) || string.IsNullOrEmpty(request.ProductId) || request.Quantity <= 0)
            return BadRequest("Invalid request payload");

        var responsePayload = new
        {
            status = "charged",
            user = request.Username,
            product = request.ProductId,
            quantity = request.Quantity,
            date = request.Date.ToString("o") // ISO-8601 format
        };

        QueueForBillingSystem(request.Username, responsePayload);

        return Ok(JsonSerializer.Serialize(responsePayload));
    }

    private static readonly ConcurrentDictionary<string, object> _userLocks = new ConcurrentDictionary<string, object>();

    private void QueueForBillingSystem(string username, object payload)
    {
        var lockObject = _userLocks.GetOrAdd(username, _ => new object());
        lock (lockObject)
        {
            Directory.CreateDirectory(StorageDirectory);
            var filePath = Path.Combine(StorageDirectory, $"{username}.json");
            List<object> payloads = new();
    
            if (System.IO.File.Exists(filePath))
            {
                try
                {
                    // Use synchronous File.ReadAllText
                    payloads = JsonSerializer.Deserialize<List<object>>(
                        System.IO.File.ReadAllText(filePath)) ?? new();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Warning: Failed to deserialize existing billing data for user {username}: {ex.Message}");
                }
            }
    
            payloads.Add(payload);
            // Use synchronous File.WriteAllText
            System.IO.File.WriteAllText(filePath, 
                JsonSerializer.Serialize(payloads, new JsonSerializerOptions { WriteIndented = true }));
        }
    }
}
