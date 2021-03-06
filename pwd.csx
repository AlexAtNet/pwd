#!/usr/bin/env dotnet-script

#r "nuget: ReadLine, 2.0.1"
#r "nuget: YamlDotNet, 8.1.2"
#r "nuget: PasswordGenerator, 2.0.5"
#r "nuget: System.IO.Abstractions, 12.2.5"

using System;
using System.IO.Abstractions;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using YamlDotNet.RepresentationModel;

static Exception Try(Action action) {
   try { action(); return null; } catch (Exception e) { return e; }
}

static T Apply<T>(this T value, Action<T> action) {
   if (!EqualityComparer<T>.Default.Equals(value, default)) action(value);
   return value;
}

static V Map<T, V>(this T value, Func<T, V> func) =>
   EqualityComparer<T>.Default.Equals(value, default) ? default : func(value);

static Aes CreateAes(byte[] salt, string password) {
   var aes = Aes.Create();
   (aes.Mode, aes.Padding) = (CipherMode.CBC, PaddingMode.PKCS7);
   // 10000 and SHA256 are defaults for pbkdf2 in openssl
   using var rfc2898 = new Rfc2898DeriveBytes(password, salt, 10000, HashAlgorithmName.SHA256);
   (aes.Key, aes.IV) = (rfc2898.GetBytes(32), rfc2898.GetBytes(16));
   return aes;
}

static byte[] ReadBytes(Stream stream, int length) =>
   new byte[length].Apply(_ => stream.Read(_, 0, length));

static byte[] Encrypt(string password, string text) {
   using var rng = new RNGCryptoServiceProvider();
   var salt = new byte[8].Apply(rng.GetBytes);
   using var stream = new MemoryStream();
   Encoding.ASCII.GetBytes("Salted__").Concat(salt).Apply(data => stream.Write(data.ToArray(), 0, 16));
   Encoding.UTF8.GetBytes(text).Apply(data => {
      using var aes = CreateAes(salt, password);
      using var encryptor = aes.CreateEncryptor(aes.Key, aes.IV);
      using var cryptoStream = new CryptoStream(stream, encryptor, CryptoStreamMode.Write);
      cryptoStream.Write(data, 0, data.Length);
   });
   return stream.ToArray();
}

static string Decrypt(string password, byte[] data) {
   using var stream = new MemoryStream(data);
   if ("Salted__" != Encoding.ASCII.GetString(ReadBytes(stream, 8)))
      throw new FormatException("Expecting the data stream to begin with Salted__.");
   using var aes = CreateAes(ReadBytes(stream, 8), password);
   using var decryptor = aes.CreateDecryptor(aes.Key, aes.IV);
   using var cryptoStream = new CryptoStream(stream, decryptor, CryptoStreamMode.Read);
   using var reader = new StreamReader(cryptoStream);
   return reader.ReadToEnd();
}

static string JoinPath(string path1, string path2) =>
    (path1 == "." ? "" : $"{path1}/") + path2;

static IEnumerable<string> GetFiles(
      IFileSystem fs,
      string path,
      (bool Recursively, bool IncludeFolders, bool IncludeDottedFilesAndFolders) options = default) =>
    fs.Directory.Exists(path) ? fs.DirectoryInfo.FromDirectoryName(path)
        .EnumerateFileSystemInfos()
        .OrderBy(info => info.Name)
        .SelectMany(info => info switch {
           IFileInfo file when !file.Name.StartsWith(".") || options.IncludeDottedFilesAndFolders =>
               new[] { JoinPath(path, file.Name) },
           IDirectoryInfo dir when !dir.Name.StartsWith(".") || options.IncludeDottedFilesAndFolders =>
               (options.Recursively
                  ? GetFiles(fs, JoinPath(path, dir.Name), options)
                  : new string[0]).Concat(options.IncludeFolders ? new[] { JoinPath(path, dir.Name) } : new string[0]),
           _ => new string[0]
        }) : Enumerable.Empty<string>();

static (string, string, string) ParseRegexCommand(string text, int idx = 0) {
   string Read() {
      var (begin, escape) = (++idx, false);
      for (; idx < text.Length; idx++) {
         var ch = text[idx];
         if (!escape && ch == '\\') { escape = true; continue; } else if (!escape && ch == '/')
            return text.Substring(begin, idx - begin);
         escape = false;
      }
      return text.Substring(begin);
   }

   var (pattern, replacement, options) = (Read(), Read(), Read());
   replacement = Regex.Replace(replacement, @"\\.", m =>
        m.Groups[0].Value[1] switch { 'n' => "\n", 't' => "\t", 'r' => "\r", var n => $"{n}" });
   return (pattern, replacement, options);
}

static Exception CheckYaml(string text) {
   using var input = new StringReader(text);
   return Try(() => new YamlStream().Load(input));
}

static bool Confirm(string question) =>
   question.Apply(_ => Console.Write($"{question} (y/N) "))
      .Map(_ => Console.ReadLine().ToUpperInvariant() == "Y");

public class File {
   private IFileSystem _fs;
   private Session _session;

   public File(IFileSystem fs, Session session, string path, string content) =>
      (_fs, _session, Path, Content, Modified) = (fs, session, path, content, false);

   public string Path { get; private set; }
   public string Content { get; private set; }
   public bool Modified { get; private set; }

   public string ExportContentToTempFile() =>
      (_fs.Path.GetTempFileName() + ".yaml").Apply(path => _fs.File.WriteAllText(path, Content));

   public File ReadFromFile(string path) => this.Apply(_ =>
      _fs.File.ReadAllText(path).Apply(content => Update(content)));

   public File Save() => this.Apply(_ => {
      _session.Write(Path, Content);
      Modified = false;
   });

   public File Rename(string path) => this.Apply(_ => {
      var folder = _fs.Path.GetDirectoryName(path);
      if (folder != "") _fs.Directory.CreateDirectory(folder);
      _fs.File.Move(Path, path);
      Path = path;
   });

   public File Replace(string command) => this.Apply(_ => {
      var (pattern, replacement, options) = ParseRegexCommand(command);
      var re = new Regex(
         pattern,
         options.Contains('i') ? RegexOptions.IgnoreCase : RegexOptions.None);
      Update(re.Replace(Content, replacement, options.Contains('g') ? -1 : 1));
   });

   public File Update(string content) => this.Apply(_ =>
      (Content, Modified) = (content, Content != content));

   public File Check() => this.Apply(_ => {
      if (CheckYaml(Content) is { Message: var msg })
         Console.Error.WriteLine(msg);
   });

   public File Print() => this.Apply(_ =>
      Console.WriteLine(Content));

   public string Field(string name) =>
      Regex.Match(Content, @$"{name}: *([^\n]+)")
         .Map(match => match.Success ? match.Groups[1].Value : null);
}

public class Session {
   private string _password;
   private IFileSystem _fs;

   public Session(string password, IFileSystem fs) =>
      (_password, _fs) = (password, fs);

   public File File { get; private set; } = null;

   public IEnumerable<string> GetItems(string path = null) =>
      GetFiles(_fs, path ?? ".", (false, true, false))
         .Where(item => !_fs.File.Exists(item) || IsFileEncrypted(item));

   public IEnumerable<string> GetEncryptedFilesRecursively(string path = null, bool includeHidden = false) =>
       GetFiles(_fs, path ?? ".", (true, false, includeHidden))
         .Where(file => IsFileEncrypted(file));

   public bool IsFileEncrypted(string path) => null == Try(() => {
      using var stream = _fs.File.OpenRead(path);
      // openssl adds Salted__ at the beginning of a file, let's use to to check whether it is enrypted or not
      _ = "Salted__" == Encoding.ASCII.GetString(ReadBytes(stream, 8)) ? default(object) : throw new Exception();
   });

   public string Read(string path) =>
      Decrypt(_password, _fs.File.ReadAllBytes(path));

   public Session Write(string path, string content) => this.Apply(_ => {
      var folder = _fs.Path.GetDirectoryName(path);
      if (folder != "") _fs.Directory.CreateDirectory(folder);
      _fs.File.WriteAllBytes(path, Encrypt(_password, content));
      if (path == File?.Path) Open(path);
   });

   public File Open(string path) =>
      File = (_fs.File.Exists(path) && IsFileEncrypted(path)) ? new File(_fs, this, path, Read(path)) : null;

   public void Close() =>
      File = null;

   public Session Check() => this.Apply(_ => {
      var names = GetEncryptedFilesRecursively(includeHidden: true).ToList();
      var wrongPassword = new List<string>();
      var notYaml = new List<string>();
      foreach (var name in names) {
         var content = default(string);
         try {
            content = Decrypt(_password, _fs.File.ReadAllBytes(name));
            if (content.Any(ch => char.IsControl(ch) && !char.IsWhiteSpace(ch)))
               content = default;
         } catch { }

         if (content == default) {
            wrongPassword.Add(name);
            Console.Write('*');
         } else if (CheckYaml(content) != null) {
            notYaml.Add(name);
            Console.Write('+');
         } else
            Console.Write('.');
      }

      if (names.Any()) Console.WriteLine();

      if (wrongPassword.Count > 0) {
         var more = wrongPassword.Count > 3 ? ", ..." : "";
         var failuresText = string.Join(", ", wrongPassword.Take(Math.Min(3, wrongPassword.Count)));
         throw new Exception($"Integrity check failed for: {failuresText}{more}");
      }

      if (notYaml.Count > 0)
         Console.Error.WriteLine($"YAML check failed for: {(string.Join(", ", notYaml))}");
   });

   public Session CopyText(string text = null) => this.Apply(s => {
      var process = default(Process);
      Try(() => process = Process.Start(new ProcessStartInfo("clip.exe") { RedirectStandardInput = true }))
         .Map(_ => Try(() => process = Process.Start(new ProcessStartInfo("pbcopy") { RedirectStandardInput = true })))
         .Map(_ => Try(() => process = Process.Start(new ProcessStartInfo("xclip -sel clip") { RedirectStandardInput = true })));
      process?.StandardInput.Apply(stdin => stdin.Write(text ?? "")).Apply(stdin => stdin.Close());
   });
}

class AutoCompletionHandler : IAutoCompleteHandler {
   private Session _session;

   public AutoCompletionHandler(Session session) =>
      _session = session;

   public char[] Separators { get; set; } = new char[0];

   public string[] GetSuggestions(string text, int index) {
      if (text.StartsWith(".") && !text.StartsWith(".."))
         return ".add,.archive,.cc,.ccp,.ccu,.check,.edit,.open,.pwd,.quit,.rename,.rm,.save".Split(',')
            .Where(item => item.StartsWith(text)).ToArray();
      var p = text.LastIndexOf('/');
      var (folder, query) = p == -1 ? ("", text) : (text.Substring(0, p), text.Substring(p + 1));
      return _session.GetItems(folder == "" ? "." : folder)
          .Where(item => item.StartsWith(text)).ToArray();
   }
}

(string, string, string) ParseCommand(string input) =>
   Regex.Match(input, @"^\.(\w+)(?: +(.+))?$").Map(match =>
      match.Success ? ("", match.Groups[1].Value, match.Groups[2].Value) : (input, "", ""));

Action<Session> Route(string input, IFileSystem fs) =>
   ParseCommand(input) switch {
      (_, "save", _) => session => session.File?.Save(),
      ("..", _, _) => session => session.Close(),
      _ when input.StartsWith("/") => session => session.File?.Replace(input).Print(),
      (_, "check", _) => session => _ = session.File?.Check() as object ?? session.Check(),
      (_, "open", var path) => session => session.Open(path)?.Print(),
      (_, "archive", _) => session => {
         session.File?.Rename(".archive/" + session.File.Path);
         session.Close();
      },
      (_, "rm", _) => session =>
         session.File.Apply(file =>
            Confirm("Delete '" + session.File.Path + "'?").Apply(_ => {
               fs.File.Delete(session.File.Path);
               session.Close();
            })),
      (_, "rename", var path) => session => session.File?.Rename(path),
      (_, "edit", var editor) => session => {
         var path = session.File?.ExportContentToTempFile();
         if (path == null) return;
         editor = string.IsNullOrEmpty(editor) ? Environment.GetEnvironmentVariable("EDITOR") : editor;
         if (string.IsNullOrEmpty(editor))
            throw new Exception("The editor is not specified and the environment variable EDITOR is not set.");
         var originalContent = session.File.Content;
         try {
            Process.Start(new ProcessStartInfo(editor, path)).WaitForExit();
            session.File.ReadFromFile(path).Print()
               .Map(file => Confirm("Save the content?") ? file : file.Update(originalContent)).Save();
         } finally {
            fs.File.Delete(path);
         }
      },
      (_, "pwd", _) => session => Console.WriteLine(new PasswordGenerator.Password().Next()),
      (_, "add", var path) => session => {
         var content = new StringBuilder();
         for (var line = ""; "" != (line = Console.ReadLine());)
            content.AppendLine(line.Replace("***", new PasswordGenerator.Password().Next()));
         session.Write(path, content.ToString()).Open(path).Print();
      },
      (_, "cc", var name) => session =>
         session.File?.Field(name).Apply(text => {
            session.CopyText(text);
            Task.Delay(TimeSpan.FromSeconds(5)).ContinueWith(_ => session.CopyText());
         }),
      (_, "ccu", _) => Route(".cc user", fs),
      (_, "ccp", _) => Route(".cc password", fs),
      _ => session => {
         if (session.File?.Print() != null)
            return;

         var names = session.GetEncryptedFilesRecursively()
             .Where(name => name.StartsWith(input, StringComparison.OrdinalIgnoreCase))
             .ToList();

         var name = names.FirstOrDefault(name => string.Equals(name, input, StringComparison.OrdinalIgnoreCase)) ??
           (names.Count == 1 && input != "" ? names[0] : default);

         if (name.Map(_ => session.Open(name)?.Print()) != null)
            return;

         Console.Write(string.Join("", names.Select(name => $"{name}\n")));
      }
   };

void Main(IFileSystem fs, Func<string, string> readpwd, Func<string, string> read, Action<Session> init, Action<IFileSystem> done) {
   var password = readpwd("Password: ");
   var session = new Session(password, fs);
   if (Try(() => session.Check()).Map(e => e.Message).Apply(Console.Error.WriteLine) != null) return;
   if (!session.GetEncryptedFilesRecursively(".", true).Any()) {
      var confirmPassword = readpwd("It seems that you are creating a new repository. Please confirm password: ");
      if (confirmPassword != password) {
         Console.Error.WriteLine("passwords do not match");
         return;
      }
   }
   session.Apply(init);
   while (true) {
      var input = read((session.File?.Modified ?? false ? "*" : "") + (session.File?.Path ?? "") + "> ").Trim();
      if (input == ".quit") break;
      Try(() => Route(input, fs)?.Invoke(session)).Map(e => e.Message).Apply(Console.Error.WriteLine);
   }
   done(fs);
}

(!Args.Contains("-t")).Apply(a =>
   Main(new FileSystem(), text => ReadLine.ReadPassword(text), text => ReadLine.Read(text), session => {
      ReadLine.HistoryEnabled = true;
      ReadLine.AutoCompletionHandler = new AutoCompletionHandler(session);
   }, fs => {
      Console.Clear();
      if ((fs.Directory.Exists(".git") || fs.Directory.Exists("../.git") || fs.Directory.Exists("../../.git")) &&
            Confirm("Update the repository?"))
         new [] { "add *", "commit -m update", "push" }
            .Select(args => Try(() => Process.Start(new ProcessStartInfo("git", args)).WaitForExit()))
            .FirstOrDefault(e => e != null).Apply(Console.Error.WriteLine);
   }));
