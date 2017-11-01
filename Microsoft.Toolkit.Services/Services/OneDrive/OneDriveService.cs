﻿// ******************************************************************
// Copyright (c) Microsoft. All rights reserved.
// This code is licensed under the MIT License (MIT).
// THE CODE IS PROVIDED “AS IS”, WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED,
// INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT.
// IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM,
// DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT,
// TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH
// THE CODE OR THE USE OR OTHER DEALINGS IN THE CODE.
// ******************************************************************

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Graph;
using Microsoft.Toolkit.Services.MicrosoftGraph;
using Microsoft.Toolkit.Services.OneDrive.Platform;

namespace Microsoft.Toolkit.Services.OneDrive
{
    /// <summary>
    ///  Class using OneDrive API
    /// </summary>
    public class OneDriveService
    {
        /// <summary>
        /// Private field for singleton.
        /// </summary>
        private static OneDriveService _instance;

        /// <summary>
        /// Gets public singleton property.
        /// </summary>
        public static OneDriveService Instance => _instance ?? (_instance = new OneDriveService());

        /// <summary>
        /// Gets or sets AppClientId.
        /// </summary>
        protected string AppClientId { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether service is initialized.
        /// </summary>
        protected bool IsInitialized { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether user is connected.
        /// </summary>
        protected bool IsConnected { get; set; }

        /// <summary>
        /// Gets or sets permission scopes.
        /// </summary>
        protected string[] Scopes { get; set; }

        /// <summary>
        /// Fields to store the account provider
        /// </summary>
        private IAuthenticationProvider _accountProvider;

        /// <summary>
        /// Gets a reference to an instance of the underlying data provider.
        /// </summary>
        public MicrosoftGraphService Provider
        {
            get
            {
                if (MicrosoftGraphService.Instance == null)
                {
                    throw new InvalidOperationException("Provider not initialized.");
                }

                return MicrosoftGraphService.Instance;
            }
        }

        /// <summary>
        /// Intialize OneDrive (Graph) service
        /// </summary>
        /// <param name="appClientId">App Client Id.</param>
        /// <param name="scopes">Permission Scopes.</param>
        /// <returns>True or false</returns>
        public bool Initialize(string appClientId, string[] scopes)
        {
            AppClientId = appClientId;
            Scopes = scopes;
            IsInitialized = true;

            if (Provider.Authentication == null)
            {
                Provider.Authentication = new MicrosoftGraphAuthenticationHelper(Scopes);
            }

            return true;
        }

        /// <summary>
        /// Logout the current user
        /// </summary>
        /// <returns>success or failure</returns>
        public virtual async Task LogoutAsync()
        {
            if (!IsInitialized)
            {
                throw new InvalidOperationException("Microsoft OneDrive not initialized.");
            }

            if (Provider != null)
            {
                await Provider.Logout();
            }
        }

        /// <summary>
        /// Signs in the user
        /// </summary>
        /// <returns>Returns success or failure of login attempt.</returns>
        public virtual async Task<bool> LoginAsync()
        {
            IsConnected = false;

            if (!IsInitialized)
            {
                throw new InvalidOperationException("Microsoft OneDrive not initialized.");
            }

            await Provider.LoginAsync();

            IsConnected = true;
            return IsConnected;
        }

        /// <summary>
        /// Gets the OneDrive root folder
        /// </summary>
        /// <returns>When this method completes, it returns a OneDriveStorageFolder</returns>
        public virtual async Task<OneDriveStorageFolder> RootFolderAsync()
        {
            // log the user silently with a Microsoft Account associate to Windows
            if (IsConnected == false)
            {
                if (!await OneDriveService.Instance.LoginAsync())
                {
                    throw new Exception("Unable to sign in");
                }
            }

            var oneDriveRootItem = await Provider.GraphProvider.Drive.Root.Request().GetAsync();
            return new OneDriveStorageFolder(Provider.GraphProvider, Provider.GraphProvider.Drive.Root, oneDriveRootItem);
        }

        /// <summary>
        /// Gets the OneDrive app root folder
        /// </summary>
        /// <returns>When this method completes, it returns a OneDriveStorageFolder</returns>
        public virtual async Task<OneDriveStorageFolder> AppRootFolderAsync()
        {
            // log the user silently with a Microsoft Account associate to Windows
            if (IsConnected == false)
            {
                if (!await OneDriveService.Instance.LoginAsync())
                {
                    throw new Exception("Unable to sign in");
                }
            }

            var oneDriveRootItem = await Provider.GraphProvider.Drive.Special.AppRoot.Request().GetAsync();
            return new OneDriveStorageFolder(Provider.GraphProvider, Provider.GraphProvider.Drive.Special.AppRoot, oneDriveRootItem);
        }

        /// <summary>
        /// Gets the OneDrive camera roll folder
        /// </summary>
        /// <returns>When this method completes, it returns a OneDriveStorageFolder</returns>
        public virtual Task<OneDriveStorageFolder> CameraRollFolderAsync()
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Gets the OneDrive documents folder
        /// </summary>
        /// <returns>When this method completes, it returns a OneDriveStorageFolder</returns>
        public virtual Task<OneDriveStorageFolder> DocumentsFolderAsync()
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Gets the OneDrive music folder
        /// </summary>
        /// <returns>When this method completes, it returns a OneDriveStorageFolder</returns>
        public virtual Task<OneDriveStorageFolder> MusicFolderAsync()
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Gets the OneDrive photos folder
        /// </summary>
        /// <returns>When this method completes, it returns a OneDriveStorageFolder</returns>
        public virtual Task<OneDriveStorageFolder> PhotosFolderAsync()
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Gets or sets platform implementation of background download service.
        /// </summary>
        public IOneDriveServiceBackgroundDownload BackgroundDownloadService { get; set; }
    }
}