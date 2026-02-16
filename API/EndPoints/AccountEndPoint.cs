using System;
using API.Common;
using API.DTOs;
using API.Extensions;
using API.Models;
using API.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace API.EndPoints;

public static class AccountEndPoint
{
    public static RouteGroupBuilder MapAccountEndPoint(this WebApplication app)
    {
        var group = app.MapGroup("/api/account").WithTags("account");

        group.MapPost("/register", async (HttpContext context, UserManager<AppUser> userManager,
        [FromForm] string userName, [FromForm] string fullName, [FromForm] string email, [FromForm] string password, [FromForm] IFormFile? profileImage) =>
        {
            var userFromDb = await userManager.FindByEmailAsync(email);
            Console.WriteLine(userFromDb);
            if (userFromDb is not null)
            {
                return Results.BadRequest(Response<string>.Failure("User is already registered."));
            }
            if (profileImage is null)
            {
                return Results.BadRequest(Response<string>.Failure("Profile image is required."));
            }
            var picture = await FileUpload.Upload(profileImage);
            picture = $"{context.Request.Scheme}://{context.Request.Host}/uploads/{picture}";
            var user = new AppUser
            {
                UserName = userName,
                Email = email,
                FullName = fullName,
                ProfileImage = picture,
            };
            var result = await userManager.CreateAsync(user, password);
            Console.WriteLine(result);
            if (!result.Succeeded)
            {
                return Results.BadRequest(Response<string>.Failure(result.Errors.Select(x => x.Description).FirstOrDefault()!));
                //             return Results.BadRequest(Response<string>.Failure(
                //     string.Join(", ", result.Errors.Select(x => x.Description))
                // ));
            }
            return Results.Ok(Response<string>.Success("", "User Created Succesfully"));

        }).DisableAntiforgery();

        group.MapPost("/login", async (UserManager<AppUser> userManager, TokenService tokenService, LoginDto dto) =>
         {
             if (dto is null)
             {
                 return Results.BadRequest(Response<string>.Failure("Invalid login request."));
             }
             var user = await userManager.FindByEmailAsync(dto.Email);
             if (user is null)
             {
                 return Results.BadRequest(Response<string>.Failure("User not found."));
             }
             var result = await userManager.CheckPasswordAsync(user, dto.Password);
             if (!result)
             {
                 return Results.BadRequest(Response<string>.Failure("Invalid password."));
             }
             var token = tokenService.GenerateToken(user.Id, user.UserName!);
             return Results.Ok(Response<string>.Success(token, "Login successfully"));
         });

        group.MapGet("/me", async (HttpContext context, UserManager<AppUser> userManager) =>
                      {
                          var currentLoggedInUserId = context.User.GetUserId();
                          var currentLoggedInUser = await userManager.Users.SingleOrDefaultAsync(x => x.Id == currentLoggedInUserId.ToString());

                          return Results.Ok(Response<AppUser>.Success(currentLoggedInUser!, "User fetched successfully"));
                      }).RequireAuthorization();


        return group;
    }
}
