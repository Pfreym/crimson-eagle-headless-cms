﻿using AutoMapper;
using CMS.API.DataAccessLayer.DTOs;
using CMS.API.DataAccessLayer.DTOs.APIUser;
using CMS.API.DataAccessLayer.Interfaces;
using CMS.API.DataAccessLayer.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace CMS.API.DataAccessLayer.Services
{
    public class UsersManager : IUsersManager
    {
        private IMapper _mapper;
        private readonly UserManager<APIUser> _userManager;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private IcmsProjectRepository _csmProjectRepository;
        private APIUser? _user;

        public UsersManager(
            IMapper mapper,
            UserManager<APIUser> userManager,
            IHttpContextAccessor httpContextAccessor,
            IcmsProjectRepository cmsProjectRepository)
        {
            this._mapper = mapper;
            this._userManager = userManager;
            this._httpContextAccessor = httpContextAccessor;
            this._csmProjectRepository = cmsProjectRepository;
        }

        public async Task<IEnumerable<APIUserDTO>> ListUsers()
        {
            List<APIUser> users = await _userManager.Users.ToListAsync();
            return users.Select(_mapper.Map<APIUser, APIUserDTO>);
        }

        public async Task<APIUserDTO?> GetUserById(string id)
        {
            _user = await _userManager.FindByIdAsync(id);

            if (_user == null) return null;

            return _mapper.Map<APIUserDTO>(_user);
        }

        public async Task<ResultDTO<APIUserDTO>> CreateNewUser(APIUserCreateDTO DTO)
        {
            APIUser newUser = _mapper.Map<APIUser>(DTO);
            newUser.UserName = DTO.Email;

            string? projectId = await GetProjectFromLoggedInUser();
            if (projectId == null) return ErrorResult("500", "Logged in user not found.");
            newUser.ProjectId = projectId;

            // check for duplicate emails
            APIUser? sameEmailUser = await _userManager.FindByEmailAsync(DTO.Email);
            if (sameEmailUser != null)
            {
                return new ResultDTO<APIUserDTO>
                {
                    Succeeded = false,
                    Errors = new List<ErrorMessage>(){
                        new ErrorMessage
                        {
                            Code = "400",
                            Description = "E-mail already taken.",
                        }
                    }
                };
            }

            IdentityResult result = await _userManager.CreateAsync(newUser, DTO.Password);

            if (!result.Succeeded)
            {
                return _mapper.Map<ResultDTO<APIUserDTO>>(result);
            }

            IdentityResult roleResult = await _userManager.AddToRoleAsync(newUser, "PROJECTUSER");

            if (!roleResult.Succeeded)
            {
                return _mapper.Map<ResultDTO<APIUserDTO>>(result);
            }

            return new ResultDTO<APIUserDTO>
            {
                Succeeded = true,
                Payload = _mapper.Map<APIUser, APIUserDTO>(newUser),
            };
        }

        public async Task<ResultDTO<APIUserDTO>> UpdateUser(string id, APIUserUpdateDTO DTO)
        {
            APIUser? updateUser = await _userManager.FindByIdAsync(id);

            if (updateUser == null) return NotFoundUser();

            updateUser = _mapper.Map<APIUserUpdateDTO, APIUser>(DTO, updateUser);

            IdentityResult result = await _userManager.UpdateAsync(updateUser);

            if (!result.Succeeded)
            {
                return _mapper.Map<ResultDTO<APIUserDTO>>(result);
            }

            if (DTO.Password != null)
            {
                string token = await _userManager.GeneratePasswordResetTokenAsync(updateUser);
                IdentityResult resetResult = await _userManager.ResetPasswordAsync(updateUser, token, DTO.Password);

                if (!resetResult.Succeeded)
                {
                    return _mapper.Map<ResultDTO<APIUserDTO>>(resetResult);
                }
            }

            return new ResultDTO<APIUserDTO>
            {
                Succeeded = true,
                Payload = _mapper.Map<APIUser, APIUserDTO>(updateUser),
            };
        }

        public async Task<ResultDTO<APIUserDTO>> DeleteUser(string id)
        {
            APIUser? deleteUser = await _userManager.FindByIdAsync(id);

            if (deleteUser == null) return NotFoundUser();

            IdentityResult result = await _userManager.DeleteAsync(deleteUser);

            return _mapper.Map<ResultDTO<APIUserDTO>>(result);
        }

        public async Task<string?> GetProjectFromLoggedInUser()
        {
            if (_httpContextAccessor.HttpContext == null)
                return null;
            var currentUserId = _httpContextAccessor.HttpContext?.User.FindFirstValue("uid");
            if (currentUserId == null)
                return null;

            _user = await _userManager.FindByIdAsync(currentUserId);
            if (_user == null)
                return null;

            return _user.ProjectId;
        }

        public string? GetUserId()
        {
            return (_user != null) ? _user.Id : null;
        }

        private ResultDTO<APIUserDTO> NotFoundUser()
        {
            return new ResultDTO<APIUserDTO>
            {
                Succeeded = false,
                Errors = new ErrorMessage[1]
                {
                    new ErrorMessage
                    {
                        Code = "404",
                        Description = "User not found.",
                    }
                },
            };
        }

        private ResultDTO<APIUserDTO> ErrorResult(string code = "404", string description = "User not found.")
        {
            return new ResultDTO<APIUserDTO>
            {
                Succeeded = false,
                Errors = new ErrorMessage[1]
                {
                    new ErrorMessage
                    {
                        Code = code,
                        Description = description,
                    }
                },
            };
        }
    }
}
