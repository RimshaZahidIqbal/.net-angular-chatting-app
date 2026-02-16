using System;
using System.Collections.Concurrent;
using API.Data;
using API.DTOs;
using API.Extensions;
using API.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace API.Hubs;

[Authorize]
public class ChatHub(UserManager<AppUser> userManager, AppDBContext context) : Hub
{
    public static readonly ConcurrentDictionary<string, OnlineUserDto> onlineUsers = new();
    public async Task LoadMessages(string recieverId, int pageNumber = 1)
    {

        int pageSize = 10;
        var userName = Context.User!.Identity!.Name!;
        var CurrentUser = await userManager.FindByNameAsync(userName);
        if (CurrentUser is null)
        {
            return;
        }
        List<MessageResponseDto> messages = await context.Messages
        .Where(x => x.ReceiverId == CurrentUser!.Id &&
         x.SenderId == recieverId ||
          x.SenderId == CurrentUser!.Id
          && x.ReceiverId == recieverId)
          .OrderByDescending(x => x.CreatedAt)
          .Skip((pageNumber - 1) * pageSize)
          .Take(pageSize)
          .OrderBy(x => x.CreatedAt)
            .Select(x => new MessageResponseDto
            {
                Id = x.Id,
                Content = x.Content,
                CreatedAt = x.CreatedAt,
                SenderId = x.SenderId,
                ReceiverId = x.ReceiverId,
                IsRead = x.IsRead,
            }).ToListAsync();

        foreach (var message in messages)
        {
            var msg = await context.Messages.FirstOrDefaultAsync(x => x.Id == message.Id);
            if (msg is not null && msg.ReceiverId == CurrentUser.Id)
            {
                msg.IsRead = true;
                await context.SaveChangesAsync();
            }
            await Clients.User(CurrentUser.Id).SendAsync("ReceiveMessageList", messages);
        }
    }
    public override async Task OnConnectedAsync()
    {
        var httpContext = Context.GetHttpContext();
        var recieverId = httpContext?.Request.Query["userId"].ToString();
        var userName = Context.User?.Identity?.Name!;
        var currentUser = await userManager.FindByNameAsync(userName);
        var connectionId = Context.ConnectionId;
        if (onlineUsers.ContainsKey(userName))
        {
            onlineUsers[userName].ConnectionId = connectionId;
        }
        else
        {
            var user = new OnlineUserDto
            {
                UserName = userName,
                FullName = currentUser!.FullName,
                ProfilePicture = currentUser!.ProfileImage,
                ConnectionId = connectionId,
            };
            onlineUsers.TryAdd(userName, user);
            await Clients.AllExcept(connectionId).SendAsync("Notify", currentUser);
        }
        if (!string.IsNullOrEmpty(recieverId))
        {
            await LoadMessages(recieverId);
        }
        await Clients.All.SendAsync("OnlineUsers", await GetAllUsers());
    }
    public async Task SendMessage(MessageRequestDto message)
    {
        var senderId = Context.User!.Identity!.Name;
        var receiverId = message.ReceiverId;
        var newMsg = new Message
        {
            Sender = await userManager.FindByNameAsync(senderId!),
            Receiver = await userManager.FindByNameAsync(receiverId!),
            IsRead = false,
            Content = message.Content,
            CreatedAt = DateTime.UtcNow
        };
        context.Messages.Add(newMsg);
        await context.SaveChangesAsync();

        // wait for the message to send to client 
        await Clients.User(receiverId!).SendAsync("Received new message", newMsg);
    }

    public async Task NotifyTyping(string receiverUserName)
    {
        var senderUserName = Context.User!.Identity!.Name;
        if (senderUserName is null)
        {
            return;
        }

        var connectionId = onlineUsers.Values.FirstOrDefault(x => x.UserName == receiverUserName)?.ConnectionId;
        if (connectionId is not null)
        {
            await Clients.Client(connectionId).SendAsync("Notifying typing to user.", senderUserName);
        }
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var userName = Context.User!.Identity!.Name!;
        onlineUsers.TryRemove(userName, out _);
        await Clients.All.SendAsync("OnlineUsers", await GetAllUsers());
        await base.OnDisconnectedAsync(exception);
    }
    private async Task<IEnumerable<OnlineUserDto>> GetAllUsers()
    {
        var userName = Context.User!.GetUserName();
        var onlineUserSet = new HashSet<string>(onlineUsers.Keys);

        var users = await userManager.Users.Select(x => new OnlineUserDto
        {
            Id = x.Id,
            UserName = x.UserName,
            FullName = x.FullName,
            ProfilePicture = x.ProfileImage,
            IsOnline = onlineUserSet.Contains(x.UserName),
            UnreadCount = context.Messages.Count(m => m.ReceiverId == userName && m.SenderId == x.Id && !m.IsRead)
        }).OrderByDescending(x => x.IsOnline).ToListAsync();
        return users;
    }
}