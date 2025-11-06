// File: SCMS.WebApp/Services/NotificationService.cs
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using SCMS.Domain;

public class NotificationService
{
    private readonly HttpClient _httpClient;

    public int UnreadCount { get; private set; }
    public event Action? OnCountChanged;

    public NotificationService(IHttpClientFactory httpClientFactory)
    {
        _httpClient = httpClientFactory.CreateClient("AuthorizedClient");
    }
    public async Task RefreshUnreadCountAsync()
    {
        try
        {
            var response = await _httpClient.GetFromJsonAsync<UnreadCountResponse>("api/notifications/unread/count");
            UnreadCount = response?.Count ?? 0;
            OnCountChanged?.Invoke(); // Thông báo cho MainLayout cập nhật UI
        }
        catch
        {
            UnreadCount = 0;
            OnCountChanged?.Invoke();
        }
    }
    public async Task GetInitialUnreadCountAsync()
    {
        try
        {
            var response = await _httpClient.GetFromJsonAsync<UnreadCountResponse>("api/notifications/unread/count");
            UnreadCount = response?.Count ?? 0;
            OnCountChanged?.Invoke(); // Thông báo cho các component khác
        }
        catch
        {
            UnreadCount = 0;
            OnCountChanged?.Invoke();
        }
    }
    public async Task<List<Notification>> GetAllNotificationsAsync()
    {
        try
        {
            return await _httpClient.GetFromJsonAsync<List<Notification>>("api/notifications/all") ?? new List<Notification>();
        }
        catch
        {
            return new List<Notification>();
        }
    }
    public async Task<List<Notification>> GetUnreadNotificationsAsync()
    {
        try
        {
            return await _httpClient.GetFromJsonAsync<List<Notification>>("api/notifications/unread") ?? new List<Notification>();
        }
        catch
        {
            return new List<Notification>();
        }
    }

    public async Task<bool> MarkAllAsReadAsync()
    {
        try
        {
            var response = await _httpClient.PostAsync("api/notifications/mark-all-as-read", null);
            if (response.IsSuccessStatusCode)
            {
                UnreadCount = 0;
                OnCountChanged?.Invoke(); 
                return true;
            }
            return false;
        }
        catch
        {
            return false;
        }
    }

    private class UnreadCountResponse
    {
        public int Count { get; set; }
    }
}