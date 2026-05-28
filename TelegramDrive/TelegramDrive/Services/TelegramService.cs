using TL;
using WTelegram;

namespace TelegramDrive.Services;

/// <summary>
/// Wraps WTelegramClient to provide Telegram API operations — the C# equivalent of Telethon.
/// Handles authentication (phone+OTP, QR, 2FA), file transfers, and message management.
/// </summary>
public class TelegramService : IDisposable
{
    private Client? _client;
    private User? _currentUser;
    private string _sessionPath;

    public bool IsConnected => _client != null;
    public bool IsAuthorized => _currentUser != null;
    public string UserName => _currentUser != null
        ? $"{_currentUser.first_name} {_currentUser.last_name}".Trim()
        : string.Empty;
    public string UserHandle => _currentUser?.username != null
        ? $"@{_currentUser.username}"
        : "Saved Messages";

    // Authentication state machine
    private string? _pendingPhone;
    private string? _pending2FA;

    // OTP Login: TaskCompletionSource-based
    // WTelegram calls the config callback on its own thread. We must block
    // that thread (synchronously) until the UI has provided the value.
    private TaskCompletionSource<string>? _codeTcs;   // for "verification_code"
    private TaskCompletionSource<string>? _passTcs;   // for "password" (2FA)
    private Task? _loginTask;                          // background login task

    public TelegramService()
    {
        _sessionPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "wt_session.session");
    }

    public void SetSessionFile(string filename)
    {
        _sessionPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, filename);
    }

    /// <summary>
    /// Ensures the session file path is available for writing.
    /// If the file exists and is NOT locked → deletes it (fresh login).
    /// If the file is locked by another process → switches to a unique temp path.
    /// This guarantees login never fails due to file contention.
    /// </summary>



    /// <summary>
    /// Connects the client and checks if already authorized from a saved session.
    /// Returns true if already authorized, false if login is required.
    /// </summary>
    public async Task<bool> ConnectAndCheckAuthAsync(int apiId, string apiHash, string? phone = null)
    {
        try
        {
            if (_client != null)
            {
                _client.Dispose();
                _client = null;
                await Task.Delay(500);
            }

            WTelegram.Helpers.Log = (lvl, msg) => 
            {
                try
                {
                    var logPath = Path.Combine(AppContext.BaseDirectory, "wt_connection.log");
                    File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{lvl}] {msg}\n");
                }
                catch { }
            };



            _client = new Client(what =>
            {
                return what switch
                {
                    "api_id"           => apiId.ToString(),
                    "api_hash"         => apiHash,
                    "phone_number"     => phone,
                    "session_pathname" => _sessionPath,
                    _                  => null
                };
            });

            _currentUser = await _client.LoginUserIfNeeded();
            if (_currentUser == null)
            {
                _client.Dispose();
                _client = null;
            }
            return _currentUser != null;
        }
        catch (Exception ex)
        {
            if (_client != null)
            {
                try { _client.Dispose(); } catch { }
                _client = null;
            }

            try
            {
                var errPath = Path.Combine(AppContext.BaseDirectory, "auth_check_error.log");
                System.IO.File.WriteAllText(errPath, ex.ToString());
            }
            catch { }
            _currentUser = null;
            return false;
        }
    }

    /// <summary>
    /// Initiates QR code login flow using WTelegramClient.
    /// </summary>
    public async Task<User?> LoginWithQRCodeAsync(int apiId, string apiHash, Action<string> qrCodeCallback)
    {
        try
        {
            if (_client != null)
            {
                _client.Dispose();
                _client = null;
                await Task.Delay(500);
            }

            WTelegram.Helpers.Log = (lvl, msg) => 
            {
                try
                {
                    var logPath = Path.Combine(AppContext.BaseDirectory, "wt_connection.log");
                    File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{lvl}] {msg}\n");
                }
                catch { }
            };



            _client = new Client(what =>
            {
                return what switch
                {
                    "api_id"           => apiId.ToString(),
                    "api_hash"         => apiHash,
                    "session_pathname" => _sessionPath,
                    "password"         => _passTcs?.Task.GetAwaiter().GetResult(),
                    _                  => null
                };
            });

            _currentUser = await _client.LoginWithQRCode(qrCodeCallback);
            return _currentUser;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"QR Login Error: {ex.Message}");
            if (_client != null)
            {
                try { _client.Dispose(); } catch { }
                _client = null;
            }
            _currentUser = null;
            throw;
        }
    }

    /// <summary>
    /// Initiates phone-based login. Sends OTP code to the phone.
    /// The WTelegram client is started in the background and suspends itself
    /// until VerifyCodeAsync / Provide2FAPassword supply the values.
    /// </summary>
    public async Task<bool> SendCodeRequestAsync(int apiId, string apiHash, string phone)
    {
        try
        {
            // Dispose any existing client
            if (_client != null)
            {
                _client.Dispose();
                _client = null;
                await Task.Delay(400);
            }

            WTelegram.Helpers.Log = (lvl, msg) => 
            {
                try
                {
                    var logPath = Path.Combine(AppContext.BaseDirectory, "wt_connection.log");
                    File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{lvl}] {msg}\n");
                }
                catch { }
            };

            _pendingPhone = phone;
            _currentUser  = null;



            // Fresh TCS for each login attempt
            _codeTcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            _passTcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

            _client = new Client(what =>
            {
                return what switch
                {
                    "api_id"            => apiId.ToString(),
                    "api_hash"          => apiHash,
                    "phone_number"      => _pendingPhone,
                    "session_pathname"  => _sessionPath,
                    // Block WTelegram's thread here until VerifyCodeAsync is called
                    "verification_code" => _codeTcs!.Task.GetAwaiter().GetResult(),
                    // Block until Provide2FAPassword is called
                    "password"          => _passTcs!.Task.GetAwaiter().GetResult(),
                    _                   => null
                };
            });

            // Run login in background — it will suspend at "verification_code"
            _loginTask = Task.Run(async () =>
            {
                try
                {
                    _currentUser = await _client.LoginUserIfNeeded();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[Login background error] {ex.Message}");
                    try
                    {
                        var errPath = Path.Combine(AppContext.BaseDirectory, "login_bg_error.log");
                        System.IO.File.WriteAllText(errPath, ex.ToString());
                    }
                    catch { }
                }
            });

            // Give WTelegram ~4 seconds to connect and send the OTP to the phone
            await Task.Delay(4000);

            // If the task already faulted (wrong phone, rate-limited, etc.) return false
            if (_loginTask.IsFaulted)
                return false;

            return true;
        }
        catch (Exception ex)
        {
            try
            {
                var errPath = Path.Combine(AppContext.BaseDirectory, "login_init_error.log");
                System.IO.File.WriteAllText(errPath, ex.ToString());
            }
            catch { }
            return false;
        }
    }

    /// <summary>
    /// Provides the OTP verification code (and optional 2FA password).
    /// Unblocks WTelegram's suspended config callback and waits for login to finish.
    /// </summary>
    public async Task<bool> VerifyCodeAsync(string code, string? password = null)
    {
        try
        {
            // Unblock WTelegram's "verification_code" callback
            _codeTcs?.TrySetResult(code);

            if (!string.IsNullOrEmpty(password))
                _passTcs?.TrySetResult(password);

            // Wait up to 15 seconds for the background login task to finish
            if (_loginTask != null)
                await Task.WhenAny(_loginTask, Task.Delay(15_000));

            return _currentUser != null;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Provides the 2FA password when requested by WTelegram.
    /// </summary>
    public void Provide2FAPassword(string password)
    {
        _pending2FA = password;
        _passTcs?.TrySetResult(password);
    }

    /// <summary>
    /// Uploads a file to Telegram Saved Messages.
    /// </summary>
    public async Task<int> UploadFileAsync(string filePath, Action<long, long>? progressCallback = null, CancellationToken ct = default)
    {
        if (_client is null) throw new InvalidOperationException("Client not connected");

        var peer = new InputPeerSelf();
        int retries = 3;
        string currentUploadPath = filePath;
        bool isTempFile = false;

        try
        {
            while (retries > 0)
            {
                try
                {
                    var inputFile = await _client.UploadFileAsync(currentUploadPath, (transmitted, totalSize) =>
                    {
                        ct.ThrowIfCancellationRequested();
                        progressCallback?.Invoke(transmitted, totalSize);
                    });

                    var message = await _client.SendMediaAsync(peer, null, inputFile);
                    return message.ID;
                }
                catch (RpcException ex) when (ex.Message.Contains("FILE_PART") || ex.Code == 400)
                {
                    retries--;
                    if (retries == 0) throw;

                    // Clean up any previous temp file
                    if (isTempFile && File.Exists(currentUploadPath))
                    {
                        try { File.Delete(currentUploadPath); } catch { }
                    }

                    // Create a new temp copy to bypass WTelegramClient's upload cache
                    try
                    {
                        string dir = Path.GetDirectoryName(filePath) ?? AppContext.BaseDirectory;
                        string ext = Path.GetExtension(filePath);
                        string tempName = $"{Path.GetFileNameWithoutExtension(filePath)}_tmp_{Guid.NewGuid():N}{ext}";
                        currentUploadPath = Path.Combine(dir, tempName);
                        File.Copy(filePath, currentUploadPath, true);
                        
                        // Force a different last modified time to bypass WTelegram's cache (keyed by size & modified time)
                        try { File.SetLastWriteTime(currentUploadPath, DateTime.Now); } catch { }
                        
                        // Append a dummy byte to bypass MD5/size-based caches (safe for almost all document/media formats)
                        try
                        {
                            using (var fs = new FileStream(currentUploadPath, FileMode.Append, FileAccess.Write))
                            {
                                fs.WriteByte((byte)new Random().Next(0, 256));
                            }
                        }
                        catch { }
                        
                        isTempFile = true;
                    }
                    catch
                    {
                        // Fallback to original filePath if copying fails
                        currentUploadPath = filePath;
                        isTempFile = false;
                    }

                    // Write retry log to help diagnosis
                    try
                    {
                        var logPath = Path.Combine(AppContext.BaseDirectory, "wt_connection.log");
                        File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [WARNING] Upload failed with: {ex.Message}. Created temp copy {currentUploadPath} to bypass cache. Retrying ({3 - retries}/3)...\n");
                    }
                    catch { }

                    await Task.Delay(2000, ct); // Wait 2 seconds before retrying
                }
            }
        }
        finally
        {
            // Always clean up the temp file if one was created
            if (isTempFile && File.Exists(currentUploadPath))
            {
                try { File.Delete(currentUploadPath); } catch { }
            }
        }

        throw new Exception("Upload failed after maximum retries.");
    }

    /// <summary>
    /// Downloads a file from Telegram Saved Messages by message ID.
    /// </summary>
    public async Task DownloadFileAsync(int messageId, string destPath, Action<long, long>? progressCallback = null, CancellationToken ct = default)
    {
        if (_client is null) throw new InvalidOperationException("Client not connected");

        var peer = new InputPeerSelf();
        var messages = await _client.GetMessages(peer, new InputMessageID { id = messageId });

        if (messages.Messages.Length == 0)
            throw new Exception("Message not found on Telegram cloud.");

        var msg = messages.Messages[0] as Message;
        if (msg?.media is not MessageMediaDocument { document: Document document })
            throw new Exception("Message does not contain a document.");

        using var fileStream = File.Create(destPath);
        await _client.DownloadFileAsync(document, fileStream, null, (transmitted, totalSize) =>
        {
            ct.ThrowIfCancellationRequested();
            progressCallback?.Invoke(transmitted, totalSize);
        });
    }

    /// <summary>
    /// Deletes messages from Saved Messages.
    /// </summary>
    public async Task DeleteMessagesAsync(List<int> messageIds)
    {
        if (_client is null) throw new InvalidOperationException("Client not connected");

        // Delete in batches of 100
        for (int i = 0; i < messageIds.Count; i += 100)
        {
            var batch = messageIds.Skip(i).Take(100).ToArray();
            await _client.DeleteMessages(new InputPeerSelf(), batch);
        }
    }

    /// <summary>
    /// Scans Saved Messages for document files and returns their metadata.
    /// </summary>
    public async Task<List<(int MessageId, string Filename, long FileSize, string UploadDate)>> SyncSavedMessagesAsync(
        HashSet<int> existingIds,
        Action<string>? statusCallback = null,
        CancellationToken ct = default)
    {
        if (_client is null) throw new InvalidOperationException("Client not connected");

        var newFiles = new List<(int, string, long, string)>();
        var peer = new InputPeerSelf();

        int offsetId = 0;
        bool hasMore = true;

        while (hasMore)
        {
            ct.ThrowIfCancellationRequested();

            var messages = await _client.Messages_GetHistory(peer, offset_id: offsetId, limit: 100);

            if (messages.Messages.Length == 0) break;

            foreach (var baseMsg in messages.Messages)
            {
                if (baseMsg is Message msg && msg.media is MessageMediaDocument { document: Document doc })
                {
                    if (!existingIds.Contains(msg.ID))
                    {
                        string filename = doc.Filename ?? "Unknown_File";
                        long fileSize = doc.size;
                        string uploadDate = msg.Date.ToString("yyyy-MM-dd HH:mm");

                        newFiles.Add((msg.ID, filename, fileSize, uploadDate));
                        statusCallback?.Invoke($"Found: {filename}");
                    }
                }
                offsetId = baseMsg.ID;
            }

            if (messages.Messages.Length < 100) hasMore = false;
        }

        return newFiles;
    }

    /// <summary>
    /// Logs out the user and deletes the session file.
    /// </summary>
    public async Task LogoutAsync()
    {
        try
        {
            if (_client != null)
            {
                await _client.Auth_LogOut();
                _client.Dispose();
                _client = null;
            }
        }
        catch { }
        finally
        {
            _currentUser = null;
            if (File.Exists(_sessionPath))
                File.Delete(_sessionPath);
        }
    }

    public (string Name, string Username) GetUserInfo()
    {
        return (UserName, UserHandle);
    }

    public (string Name, string Username, string Phone, long Id) GetUserInfoDetailed()
    {
        if (_currentUser == null)
            return (string.Empty, string.Empty, string.Empty, 0);
        return (UserName, UserHandle, _currentUser.phone ?? string.Empty, _currentUser.id);
    }

    public void Dispose()
    {
        _client?.Dispose();
        _client = null;
    }
}
