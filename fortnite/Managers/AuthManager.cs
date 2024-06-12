﻿using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using fortnite.Objects.Endpoints;

namespace fortnite.Managers;

public static class AuthManager
{
    private static DefaultEndpoint Endpoint { get; set; }

    static AuthManager()
    {
        Endpoint = new("https://account-public-service-prod03.ol.epicgames.com/account/api/oauth/token", RestSharp.Method.Post);

        Endpoint.WithHeaders(("Authorization", "basic MzQ0NmNkNzI2OTRjNGE0NDg1ZDgxYjc3YWRiYjIxNDE6OTIwOWQ0YTVlMjVhNDU3ZmI5YjA3NDg5ZDMxM2I0MWE="));
        Endpoint.WithFormBody(("grant_type", "client_credentials"));
    }

    public static bool TryCreateToken([NotNullWhen(true)] out string? token)
    {
        token = string.Empty;

        var response = Endpoint.GetResponse();

        if (!response.IsSuccessful || string.IsNullOrEmpty(response.Content))
        {
            Log.Error("Couldn't get token response. Status code {Code}", response.StatusCode);
            return false;
        }

        using var doc = JsonDocument.Parse(response.Content);

        if (!doc.RootElement.TryGetProperty("access_token", out var tokenProp))
            return false;

        token = tokenProp.GetString();

        if (string.IsNullOrEmpty(token))
            return false;

        return true;
    }
}
