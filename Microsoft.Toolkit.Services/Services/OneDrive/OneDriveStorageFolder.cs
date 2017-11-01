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
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Graph;
using Newtonsoft.Json;

namespace Microsoft.Toolkit.Services.OneDrive
{
    /// <summary>
    /// OneDriveStorageFolder Type
    /// </summary>
    public class OneDriveStorageFolder : OneDriveStorageItem
    {
        private IDriveItemChildrenCollectionRequest _nextPageItemsRequest;
        private IDriveItemChildrenCollectionRequest _nextPageFoldersRequest;
        private IDriveItemChildrenCollectionRequest _nextPageFilesRequest;

        /// <summary>
        /// Requests from OneDrive the file or folder with the specified name from the current folder.
        /// </summary>
        /// <param name="path">The name (or path relative to the current folder) of the file or folder to get.</param>
        /// <param name="cancellationToken">The <see cref="CancellationToken"/> for the request.</param>
        /// <returns>When this method completes successfully, it returns a DriveItem that represents the specified file or folder.</returns>
        private async Task<DriveItem> RequestChildrenAsync(string path, CancellationToken cancellationToken)
        {
            return await ((IDriveItemRequestBuilder)RequestBuilder).ItemWithPath(path).Request().GetAsync(cancellationToken).ConfigureAwait(false);
        }

        private ChunkedUploadProvider _uploadProvider;

        private UploadSession _uploadSession;

        /// <summary>
        /// Gets the upload provider
        /// </summary>
        private ChunkedUploadProvider UploadProvider
        {
            get { return _uploadProvider; }
        }

        /// <summary>
        /// Gets or sets a value indicating whether if a large file upload is completed
        /// </summary>
        public bool IsUploadCompleted { get; set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="OneDriveStorageFolder"/> class.
        /// <para>Permissions : Have full access to user files and files shared with user</para>
        /// </summary>
        /// <param name="oneDriveProvider">Instance of Graph Client class</param>
        /// <param name="requestBuilder">Http request builder.</param>
        /// <param name="oneDriveItem">OneDrive's item</param>
        public OneDriveStorageFolder(IBaseClient oneDriveProvider, IBaseRequestBuilder requestBuilder, DriveItem oneDriveItem)
            : base(oneDriveProvider, requestBuilder, oneDriveItem)
        {
        }

        /// <summary>
        /// Renames the current folder.
        /// </summary>
        /// <param name="desiredName">The desired, new name for the current folder.</param>
        /// <param name="cancellationToken">The <see cref="CancellationToken"/> for the request.</param>
        /// <returns>When this method completes successfully, it returns an IOneDriveStorageFolder that represents the specified folder.</returns>
        public async new Task<OneDriveStorageFolder> RenameAsync(string desiredName, CancellationToken cancellationToken = default(CancellationToken))
        {
            var renameItem = await base.RenameAsync(desiredName, cancellationToken);
            return InitializeOneDriveStorageFolder(renameItem.OneDriveItem);
        }

        /// <summary>
        /// Creates a new file in the current folder. This method also specifies what to
        /// do if a file with the same name already exists in the current folder.
        /// </summary>
        /// <param name="desiredName">The name of the new file to create in the current folder.</param>
        /// <param name="options">One of the enumeration values that determines how to handle the collision if a file with the specified desiredName already exists in the current folder.</param>
        /// <param name="content">The data's stream to push into the file</param>
        /// <param name="cancellationToken">The <see cref="CancellationToken"/> for the request.</param>
        /// <remarks>With OneDrive Consumer, the content could not be null</remarks>
        /// One of the enumeration values that determines how to handle the collision if
        /// a file with the specified desiredNewName already exists in the destination folder.
        /// Default : Fail
        /// <returns>When this method completes, it returns a IOneDriveStorageFile that represents the new file.</returns>

        public async Task<OneDriveStorageFile> CreateFileAsync(string desiredName, CreationCollisionOption options = CreationCollisionOption.FailIfExists, IRandomAccessStream content = null, CancellationToken cancellationToken = default(CancellationToken))
        {
            Stream streamContent = null;
            if (string.IsNullOrEmpty(desiredName))
            {
                throw new ArgumentNullException(nameof(desiredName));
            }

            if (content == null)
            {
                // Because OneDrive (Not OneDrive For business) don't allow to create a file with no content
                // Put a single byte, then the caller can call Item.WriteAsync() to put the real content in the file
                byte[] buffer = new byte[1];
                buffer[0] = 0x00;
                streamContent = new MemoryStream(buffer);
            }
            else
            {
                if (content.Size > OneDriveUploadConstants.SimpleUploadMaxSize)
                {
                    throw new ServiceException(new Error { Message = "The file size cannot exceed 4MB, use UploadFileAsync instead ", Code = "MaxSizeExceeded", ThrowSite = "UWP Community Toolkit" });
                }

                streamContent = content.AsStreamForRead();
            }

            var childrenRequest = ((IDriveItemRequestBuilder)RequestBuilder).Children.Request();
            string requestUri = $"{childrenRequest.RequestUrl}/{desiredName}/content?@name.conflictBehavior={OneDriveHelper.TransformCollisionOptionToConflictBehavior(options.ToString())}";
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Put, requestUri)
            {
                Content = new StreamContent(streamContent)
            };
            var createdFile = await ((IGraphServiceClient)Provider).SendAuthenticatedRequestAsync(request, cancellationToken);
            return InitializeOneDriveStorageFile(createdFile);
        }

        /// <summary>
        /// Creates a new subfolder in the current folder.
        /// </summary>
        /// <param name="desiredName">The name of the new subfolder to create in the current folder.</param>
        /// <param name="options">>One of the enumeration values that determines how to handle the collision if a file with the specified desiredName already exists in the current folder.</param>
        /// <param name="cancellationToken">The <see cref="CancellationToken"/> for the request.</param>
        /// <returns>When this method completes, it returns a IOneDriveStorageFolder that represents the new subfolder.</returns>
        public async Task<OneDriveStorageFolder> CreateFolderAsync(string desiredName, CreationCollisionOption options = CreationCollisionOption.FailIfExists, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (string.IsNullOrEmpty(desiredName))
            {
                throw new ArgumentNullException(nameof(desiredName));
            }

            var childrenRequest = ((IDriveItemRequestBuilder)RequestBuilder).Children.Request();
            var requestUri = childrenRequest.RequestUrl;

            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, requestUri);
            DriveItem item = new DriveItem { Name = desiredName, Folder = new Graph.Folder { } };
            item.AdditionalData = new Dictionary<string, object>();
            item.AdditionalData.Add(new KeyValuePair<string, object>("@microsoft.graph.conflictBehavior", OneDriveHelper.TransformCollisionOptionToConflictBehavior(options.ToString())));

            var jsonOptions = JsonConvert.SerializeObject(item);
            request.Content = new StringContent(jsonOptions, System.Text.Encoding.UTF8, "application/json");

            var createdFolder = await ((IGraphServiceClient)Provider).SendAuthenticatedRequestAsync(request, cancellationToken).ConfigureAwait(false);
            return InitializeOneDriveStorageFolder(createdFolder);
        }

        /// <summary>
        /// Gets the file with the specified name from the current folder.
        /// </summary>
        /// <param name="name">The name (or path relative to the current folder) of the file to get.</param>
        /// <param name="cancellationToken">The <see cref="CancellationToken"/> for the request.</param>
        /// <returns>When this method completes successfully, it returns a IOneDriveStorageFile that represents the specified file.</returns>
        public async Task<OneDriveStorageFile> GetFileAsync(string name, CancellationToken cancellationToken = default(CancellationToken))
        {
            var oneDriveItem = await RequestChildrenAsync(name, cancellationToken);
            if (oneDriveItem == null)
            {
                return null;
            }

            return InitializeOneDriveStorageFile(oneDriveItem);
        }

        /// <summary>
        /// Gets the files in the current folder.
        /// </summary>
        /// <param name="top">The number of items to return in a result set.</param>
        /// <param name="orderBy">Sort the order of items in the response collection</param>
        /// <param name="filter">Filters the response based on a set of criteria.</param>
        /// <param name="cancellationToken">The <see cref="CancellationToken"/> for the request.</param>
        /// <returns>When this method completes successfully, it returns a list of the files in the current folder.</returns>
        public async Task<List<OneDriveStorageFile>> GetFilesAsync(int top = 20, OrderBy orderBy = OrderBy.None, string filter = null, CancellationToken cancellationToken = default(CancellationToken))
        {
            IDriveItemChildrenCollectionRequest oneDriveItemsRequest = CreateChildrenRequest(top, orderBy, filter);
            return await RequestOneDriveFilesAsync(oneDriveItemsRequest, cancellationToken);
        }

        /// <summary>
        /// Gets the folder with the specified name from the current folder.
        /// </summary>
        /// <param name="name">The name (or path relative to the current folder) of the folder to get.</param>
        /// <param name="cancellationToken">The <see cref="CancellationToken"/> for the request.</param>
        /// <returns>When this method completes successfully, it returns a IOneDriveStorageFolder that represents the specified file.</returns>
        public async Task<OneDriveStorageFolder> GetFolderAsync(string name, CancellationToken cancellationToken = default(CancellationToken))
        {
            var oneDriveItem = await RequestChildrenAsync(name, cancellationToken).ConfigureAwait(false);
            return InitializeOneDriveStorageFolder(oneDriveItem);
        }

        /// <summary>
        /// Gets the subfolders in the current folder.
        /// </summary>
        /// <param name="top">The number of items to return in a result set.</param>
        /// <param name="orderBy">Sort the order of items in the response collection</param>
        /// <param name="filter">Filters the response based on a set of criteria.</param>
        /// <param name="cancellationToken">The <see cref="CancellationToken"/> for the request.</param>
        /// <returns>When this method completes successfully, it returns a list of the subfolders in the current folder.</returns>
        public async Task<List<OneDriveStorageFolder>> GetFoldersAsync(int top = 100, OrderBy orderBy = OrderBy.None, string filter = null, CancellationToken cancellationToken = default(CancellationToken))
        {
            IDriveItemChildrenCollectionRequest oneDriveItemsRequest = CreateChildrenRequest(top, orderBy, filter);
            return await RequestOneDriveFoldersAsync(oneDriveItemsRequest, cancellationToken);
        }

        /// <summary>
        /// Gets the item with the specified name from the current folder.
        /// </summary>
        /// <param name="name">The name (or path relative to the current folder) of the folder to get.</param>
        /// <param name="cancellationToken">The <see cref="CancellationToken"/> for the request.</param>
        /// <returns>When this method completes successfully, it returns a IOneDriveStorageItem that represents the specified file.</returns>
        public async Task<OneDriveStorageItem> GetItemAsync(string name, CancellationToken cancellationToken = default(CancellationToken))
        {
            var oneDriveItem = await RequestChildrenAsync(name, cancellationToken).ConfigureAwait(false);
            return InitializeOneDriveStorageItem(oneDriveItem);
        }

        /// <summary>
        /// Gets the items from the current folder.
        /// </summary>
        /// <param name="top">The number of items to return in a result set.</param>
        /// <param name="orderBy">Sort the order of items in the response collection</param>
        /// <param name="filter">Filters the response based on a set of criteria.</param>
        /// <param name="cancellationToken">The <see cref="CancellationToken"/> for the request.</param>
        /// <returns>When this method completes successfully, it returns a list of the subfolders and files in the current folder.</returns>
        public async Task<IReadOnlyList<OneDriveStorageItem>> GetItemsAsync(int top, OrderBy orderBy = OrderBy.None, string filter = null, CancellationToken cancellationToken = default(CancellationToken))
        {
            IDriveItemChildrenCollectionRequest oneDriveItemsRequest = ((IDriveItemRequestBuilder)RequestBuilder).CreateChildrenRequest(top, orderBy, filter);
            return await RequestOneDriveItemsAsync(oneDriveItemsRequest, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Gets the items from the current folder.
        /// </summary>
        /// <param name="orderBy">Sort the order of items in the response collection</param>
        /// <remarks>don't use awaitable</remarks>
        /// <returns>When this method completes successfully, it returns a list of the subfolders and files in the current folder.</returns>
        public IncrementalLoadingCollection<OneDriveRequestSource<OneDriveStorageItem>, OneDriveStorageItem> GetItemsAsync(OrderBy orderBy = OrderBy.None)
        {
            var requestSource = new OneDriveRequestSource<OneDriveStorageItem>(Provider, RequestBuilder, orderBy, null);
            return new IncrementalLoadingCollection<OneDriveRequestSource<OneDriveStorageItem>, OneDriveStorageItem>(requestSource);
        }

        /// <summary>
        /// Gets an index-based range of files and folders from the list of all files and subfolders in the current folder.
        /// </summary>
        /// <param name="startIndex">The zero-based index of the first item in the range to get</param>
        /// <param name="maxItemsToRetrieve">The maximum number of items to get</param>
        /// <param name="cancellationToken">The <see cref="CancellationToken"/> for the request.</param>
        /// <returns>When this method completes successfully, it returns a list of the subfolders and files in the current folder.</returns>
        public async Task<IReadOnlyList<OneDriveStorageItem>> GetItemsAsync(uint startIndex, uint maxItemsToRetrieve, CancellationToken cancellationToken = default(CancellationToken))
        {
            IDriveItemChildrenCollectionRequest oneDriveitemsRequest = null;
            var request = ((IDriveItemRequestBuilder)RequestBuilder).Children.Request();

            // skip is not working right now
            // oneDriveitemsRequest = request.Top((int)maxItemsToRetrieve).Skip((int)startIndex);
            int maxToRetrieve = (int)(maxItemsToRetrieve + startIndex);
            oneDriveitemsRequest = request.Top(maxToRetrieve);
            var tempo = await oneDriveitemsRequest.GetAsync(cancellationToken).ConfigureAwait(false);

            List<OneDriveStorageItem> items = new List<OneDriveStorageItem>();

            for (int i = (int)startIndex; i < maxToRetrieve && i < tempo.Count; i++)
            {
                items.Add(InitializeOneDriveStorageItem(tempo[i]));
            }

            return new OneDriveStorageItemsCollection(items);
        }

        /// <summary>
        /// Retrieve the next page of items
        /// </summary>
        /// <param name="cancellationToken">The <see cref="CancellationToken"/> for the request.</param>
        /// <returns>The next collection of items or null if there are no more items (an item could be a folder or  file)</returns>
        public async Task<IReadOnlyList<OneDriveStorageItem>> NextItemsAsync(CancellationToken cancellationToken = default(CancellationToken))
        {
            if (_nextPageItemsRequest != null)
            {
                return await RequestOneDriveItemsAsync(_nextPageItemsRequest, cancellationToken);
            }

            // no more items
            return null;
        }

        /// <summary>
        /// Cancel the upload Session
        /// </summary>
        /// <returns>Task to support await of async call.</returns>
        public async Task CancelSessionAsync()
        {
            if (_uploadProvider != null)
            {
                await _uploadProvider.DeleteSession();
            }
        }

        /// <summary>
        /// Retrieve the next page of folders
        /// </summary>
        /// <param name="cancellationToken">The <see cref="CancellationToken"/> for the request.</param>
        /// <returns>The next collection of folders or null if there are no more folders</returns>
        public async Task<List<OneDriveStorageFolder>> NextFoldersAsync(CancellationToken cancellationToken = default(CancellationToken))
        {
            if (_nextPageFoldersRequest != null)
            {
                return await RequestOneDriveFoldersAsync(_nextPageFoldersRequest, cancellationToken);
            }

            // no more folders
            return null;
        }

        /// <summary>
        /// Retrieve the next page of files
        /// </summary>
        /// <param name="cancellationToken">The <see cref="CancellationToken"/> for the request.</param>
        /// <returns>The next collection of files or null if there are no more files</returns>
        public async Task<List<OneDriveStorageFile>> NextFilesAsync(CancellationToken cancellationToken = default(CancellationToken))
        {
            if (_nextPageFilesRequest != null)
            {
                return await RequestOneDriveFilesAsync(_nextPageFilesRequest, cancellationToken);
            }

            // no more folders
            return null;
        }

        /// <summary>
        /// Gets the next expected ranges of the upload
        /// </summary>
        /// <remarks>Not available for OneDriveForBusiness</remarks>
        /// <returns>return next expected ranges, 0 if no more data</returns>
        public async Task<long> GetUploadStatusAsync()
        {
            await Task.Delay(0);
            return 0;
        }

        /// <summary>
        /// Creates a new large file in the current folder.
        /// Use this method when your file is larger than
        /// </summary>
        /// <param name="desiredName">The name of the new file to create in the current folder.</param>
        /// <param name="content">The data's stream to push into the file</param>
        /// <param name="options">One of the enumeration values that determines how to handle the collision if a file with the specified desiredName already exists in the current folder.</param>
        /// <param name="maxChunkSize">Max chunk size must be a multiple of 320 KiB (ie: 320*1024)</param>
        /// <returns>When this method completes, it returns a IOneDriveStorageFile that represents the new file.</returns>
        public async Task<OneDriveStorageFile> UploadFileAsync(string desiredName, IRandomAccessStream content, CreationCollisionOption options = CreationCollisionOption.FailIfExists, int maxChunkSize = -1)
        {
            int currentChunkSize = maxChunkSize < 0 ? OneDriveUploadConstants.DefaultMaxChunkSizeForUploadSession : maxChunkSize;

            if (currentChunkSize % OneDriveUploadConstants.RequiredChunkSizeIncrementForUploadSession != 0)
            {
                throw new ArgumentException("Max chunk size must be a multiple of 320 KiB", nameof(maxChunkSize));
            }

            if (string.IsNullOrEmpty(desiredName))
            {
                throw new ArgumentNullException(nameof(desiredName));
            }

            if (content == null)
            {
                throw new ArgumentNullException(nameof(content));
            }

            var uploadSessionUri = $"{Provider.BaseUrl}/drive/items/{OneDriveItem.Id}:/{desiredName}:/createUploadSession";

            var conflictBehavior = new OneDriveItemConflictBehavior { Item = new OneDriveConflictItem { ConflictBehavior = OneDriveHelper.TransformCollisionOptionToConflictBehavior(options.ToString()) } };

            var jsonConflictBehavior = JsonConvert.SerializeObject(conflictBehavior);
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, uploadSessionUri)
            {
                Content = new StringContent(jsonConflictBehavior, Encoding.UTF8, "application/json")
            };
            await Provider.AuthenticationProvider.AuthenticateRequestAsync(request).ConfigureAwait(false);

            var response = await Provider.HttpProvider.SendAsync(request).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                throw new ServiceException(new Error { Message = "Could not create an UploadSession", Code = "NoUploadSession", ThrowSite = "UWP Community Toolkit" });
            }

            IsUploadCompleted = false;
            var jsonData = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            _uploadSession = JsonConvert.DeserializeObject<UploadSession>(jsonData);

            var streamToUpload = content.AsStreamForRead();
            _uploadProvider = new ChunkedUploadProvider(_uploadSession, Provider, streamToUpload, maxChunkSize);

            var uploadedItem = await _uploadProvider.UploadAsync().ConfigureAwait(false);
            IsUploadCompleted = true;
            return InitializeOneDriveStorageFile(uploadedItem);
        }

        /// <summary>
        /// Create children http request with specific options
        /// </summary>
        /// <param name="top">The number of items to return in a result set.</param>
        /// <param name="orderBy">Sort the order of items in the response collection</param>
        /// <param name="filter">Filters the response based on a set of criteria.</param>
        /// <returns>Returns the http request</returns>
        private IDriveItemChildrenCollectionRequest CreateChildrenRequest(int top, OrderBy orderBy = OrderBy.None, string filter = null)
        {
            IDriveItemChildrenCollectionRequest oneDriveitemsRequest = null;
            if (orderBy == OrderBy.None && string.IsNullOrEmpty(filter))
            {
                return ((IDriveItemRequestBuilder)RequestBuilder).Children.Request().Top(top);
            }

            if (orderBy == OrderBy.None)
            {
                return ((IDriveItemRequestBuilder)RequestBuilder).Children.Request().Top(top).Filter(filter);
            }

            string order = $"{orderBy} asc".ToLower();

            if (string.IsNullOrEmpty(filter))
            {
                oneDriveitemsRequest = ((IDriveItemRequestBuilder)RequestBuilder).Children.Request().Top(top).OrderBy(order);
            }
            else
            {
                oneDriveitemsRequest = ((IDriveItemRequestBuilder)RequestBuilder).Children.Request().Top(top).OrderBy(order).Filter(filter);
            }

            return oneDriveitemsRequest;
        }

        /// <summary>
        /// Request a list of DriveItem from oneDrive and create a GraphOneDriveStorageItemsCollection Collection
        /// </summary>
        /// <param name="request">Http request to execute</param>
        /// <param name="cancellationToken">The <see cref="CancellationToken"/> for the request.</param>
        /// <returns>When this method completes successfully, it returns GraphOneDriveStorageItemsCollection that represents the specified files or folders</returns>
        private async Task<OneDriveStorageItemsCollection> RequestOneDriveItemsAsync(IDriveItemChildrenCollectionRequest request, CancellationToken cancellationToken)
        {
            var oneDriveItems = await request.GetAsync(cancellationToken).ConfigureAwait(false);

            _nextPageItemsRequest = oneDriveItems.NextPageRequest;

            List<OneDriveStorageItem> items = new List<OneDriveStorageItem>();

            foreach (var oneDriveItem in oneDriveItems)
            {
                items.Add(InitializeOneDriveStorageItem(oneDriveItem));
            }

            return new OneDriveStorageItemsCollection(items);
        }

        /// <summary>
        /// Request a list of Folder from oneDrive and create a MicrosoftGraphOneDriveItemCollection Collection
        /// </summary>
        /// <param name="request">Http request to execute</param>
        /// <param name="cancellationToken">The <see cref="CancellationToken"/> for the request.</param>
        /// <returns>When this method completes successfully, it returns GraphOneDriveStorageItemsCollection that represents the specified files or folders</returns>
        private async Task<List<OneDriveStorageFolder>> RequestOneDriveFoldersAsync(IDriveItemChildrenCollectionRequest request, CancellationToken cancellationToken)
        {
            var oneDriveItems = await request.GetAsync(cancellationToken);

            _nextPageFoldersRequest = oneDriveItems.NextPageRequest;

            List<DriveItem> oneDriveFolders = QueryFolders(oneDriveItems);
            List<OneDriveStorageFolder> folders = new List<OneDriveStorageFolder>();

            foreach (var oneDriveFolder in oneDriveFolders)
            {
                folders.Add(InitializeOneDriveStorageFolder(oneDriveFolder));
            }

            return folders;
        }

        /// <summary>
        /// Filter folder
        /// </summary>
        /// <param name="itemFolders">Collection of DriveItem </param>
        /// <returns>Only the DriveItems folders </returns>
        private List<DriveItem> QueryFolders(IDriveItemChildrenCollectionPage itemFolders)
        {
            var query = from f in itemFolders.CurrentPage where f.Folder != null || f.SpecialFolder != null select f;
            return query.ToList<DriveItem>();
        }

        /// <summary>
        /// Request a list of items
        /// </summary>
        /// <param name="request">Http request to execute</param>
        /// <param name="cancellationToken">The <see cref="CancellationToken"/> for the request.</param>
        /// <returns>When this method completes successfully, it returns a list of IOneDriveStorageFile that represents the specified files</returns>
        private async Task<List<OneDriveStorageFile>> RequestOneDriveFilesAsync(IDriveItemChildrenCollectionRequest request, CancellationToken cancellationToken = default(CancellationToken))
        {
            var oneDriveItems = await request.GetAsync(cancellationToken);

            _nextPageFilesRequest = oneDriveItems.NextPageRequest;

            // TODO: The first items on the list are never a file
            List<DriveItem> oneDriveFiles = QueryFiles(oneDriveItems);

            // TODO: Algo to get only File
            List<OneDriveStorageFile> files = new List<OneDriveStorageFile>();

            foreach (var oneDriveFile in oneDriveFiles)
            {
                files.Add(InitializeOneDriveStorageFile(oneDriveFile));
            }

            return files;
        }

        /// <summary>
        /// Filter files
        /// </summary>
        /// <param name="itemFiles">Collection of DriveItem</param>
        /// <returns>A list of DriveItem</returns>
        private List<DriveItem> QueryFiles(IDriveItemChildrenCollectionPage itemFiles)
        {
            var query = from f in itemFiles.CurrentPage where f.File != null select f;
            return query.ToList<DriveItem>();
        }
    }
}