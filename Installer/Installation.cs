using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace ZhCnInstaller
{
    internal sealed class Product
    {
        internal static readonly Product Afterburner = new Product(
            "Afterburner", "MSI Afterburner", "MSIAfterburner.exe", "MSIWin11Dark-zh-CN.usf",
            @"SOFTWARE\MSI\Afterburner", "MSI Afterburner", @"Profiles\MSIAfterburner.cfg");
        internal static readonly Product Rtss = new Product(
            "RTSS", "RivaTuner Statistics Server", "RTSS.exe", "Default4Chinese.usf",
            @"SOFTWARE\Unwinder\RTSS", "RTSS", @"Profiles\Config");

        internal readonly string Id, Name, Executable, Skin, Registration, Uninstall, Config;

        private Product(string id, string name, string executable, string skin,
            string registration, string uninstall, string config)
        {
            Id = id;
            Name = name;
            Executable = executable;
            Skin = skin;
            Registration = registration;
            Uninstall = uninstall;
            Config = config;
        }

        internal string FindDirectory()
        {
            var running = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(Executable));
            try
            {
                foreach (var p in running)
                {
                    try
                    {
                        var directory = Path.GetDirectoryName(p.MainModule.FileName);
                        if (ExistsIn(directory)) return directory;
                    }
                    catch (System.ComponentModel.Win32Exception) { }
                    catch (InvalidOperationException) { }
                    catch (NotSupportedException) { }
                }
            }
            finally
            {
                foreach (var p in running) p.Dispose();
            }

            foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
            foreach (var view in new[] { RegistryView.Registry32, RegistryView.Registry64 })
            {
                try
                {
                    using (var root = RegistryKey.OpenBaseKey(hive, view))
                    using (var setup = root.OpenSubKey(Registration))
                    using (var uninstall = root.OpenSubKey(
                        @"Software\Microsoft\Windows\CurrentVersion\Uninstall\" + Uninstall))
                    {
                        var installPath = Value(setup, "InstallPath");
                        var directories = new[]
                        {
                            Value(setup, "InstallDir"),
                            installPath,
                            DirectoryOfExecutable(installPath),
                            Value(uninstall, "InstallLocation"),
                            DirectoryOfExecutable(Value(uninstall, "UninstallString"))
                        };
                        var registered = directories.FirstOrDefault(ExistsIn);
                        if (registered != null) return registered;
                    }
                }
                catch (UnauthorizedAccessException) { }
                catch (System.Security.SecurityException) { }
                catch (ArgumentException) { }
            }

            return new[] { "ProgramFiles(x86)", "ProgramW6432", "ProgramFiles" }
                .Select(Environment.GetEnvironmentVariable)
                .Where(p => !String.IsNullOrEmpty(p))
                .Select(p => Path.Combine(p, Name))
                .FirstOrDefault(ExistsIn);
        }

        private static string Value(RegistryKey key, string name)
        {
            return key == null ? null : key.GetValue(name) as string;
        }

        private string DirectoryOfExecutable(string value)
        {
            if (String.IsNullOrWhiteSpace(value)) return null;
            value = value.Trim();
            if (value[0] == '"')
            {
                int quote = value.IndexOf('"', 1);
                if (quote < 0) return null;
                value = value.Substring(1, quote - 1);
            }

            // An install folder can contain ".exe" in its name. Check complete
            // executable-path candidates against the actual installed files.
            var extensions = Regex.Matches(value, @"\.exe(?=\s|$)", RegexOptions.IgnoreCase);
            foreach (Match match in extensions.Cast<Match>().Reverse())
            {
                try
                {
                    var directory = Path.GetDirectoryName(value.Substring(0, match.Index + match.Length));
                    if (ExistsIn(directory)) return directory;
                }
                catch (ArgumentException) { }
            }
            return null;
        }

        internal bool ExistsIn(string folder)
        {
            if (String.IsNullOrWhiteSpace(folder)) return false;
            try { return File.Exists(Path.Combine(folder.Trim().Trim('"'), Executable)); }
            catch (ArgumentException) { return false; }
        }

        internal void RequireClosed()
        {
            var running = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(Executable));
            try
            {
                if (running.Any(p => !p.HasExited))
                    throw new InvalidOperationException("请先从系统托盘退出 " + Name + "，然后重试。");
            }
            finally
            {
                foreach (var p in running) p.Dispose();
            }
        }
    }

    internal sealed class Target
    {
        internal readonly Product Product;
        internal readonly string Root;

        internal Target(Product product, string root)
        {
            Product = product;
            string path = Path.GetFullPath(root.Trim().Trim('"'));
            Root = path.Length == Path.GetPathRoot(path).Length ? path :
                path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (!product.ExistsIn(Root))
                throw new InvalidOperationException("所选目录中找不到 " + product.Executable + "。\n" + Root);
        }
    }

    internal static class SettingsFile
    {
        private static readonly Regex Settings = new Regex(
            @"(?im)^[ \t]*\[Settings\][^\r\n]*(?:\r?\n|$)");
        private static readonly Regex Section = new Regex(@"(?m)^[ \t]*\[[^]\r\n]+\]");
        private static readonly Encoding AnsiBytes = Encoding.GetEncoding(28591);
        private static readonly Encoding WindowsUnicode = new UnicodeEncoding(false, true, true);

        internal static byte[] SelectChinese(byte[] input, string skin)
        {
            Encoding encoding = AnsiBytes;
            byte[] preamble = new byte[0];
            foreach (var candidate in new Encoding[]
                { new UTF8Encoding(true, true), WindowsUnicode, new UnicodeEncoding(true, true, true) })
            {
                byte[] prefix = candidate.GetPreamble();
                if (!input.Take(prefix.Length).SequenceEqual(prefix)) continue;
                encoding = candidate;
                preamble = prefix;
                break;
            }

            string content = encoding.GetString(input, preamble.Length, input.Length - preamble.Length);
            if (content.IndexOf('\0') >= 0)
                throw new InvalidDataException("配置文件编码异常，无法安全更新语言和皮肤选项。");

            string newline = content.Contains("\r\n") || !content.Contains("\n") ? "\r\n" : "\n";
            var section = Settings.Match(content);
            int start = section.Success ? section.Index + section.Length : content.Length;
            var following = Section.Match(content, start);
            int end = following.Success ? following.Index : content.Length;
            string block = content.Substring(start, end - start);
            string heading = content.Substring(0, start);
            if (heading.Length > 0 && !heading.EndsWith("\n", StringComparison.Ordinal)) heading += newline;
            if (!section.Success) heading += "[Settings]" + newline;

            block = SetKey(block, "Language", "CHN", newline);
            block = SetKey(block, "Skin", skin, newline);
            // The applications use the Windows profile APIs, which accept ANSI or
            // BOM-marked UTF-16 LE. Preserve the text of other recognized encodings.
            var output = encoding == AnsiBytes ? AnsiBytes : WindowsUnicode;
            return output.GetPreamble().Concat(output.GetBytes(heading + block + content.Substring(end))).ToArray();
        }

        private static string SetKey(string section, string key, string value, string newline)
        {
            var expression = new Regex(@"(?im)^([ \t]*" + key + @"[ \t]*=[ \t]*)[^;\r\n]*(;[^\r\n]*)?");
            if (expression.IsMatch(section))
                return expression.Replace(section, match =>
                    (match.Groups[2].Success ? match.Groups[2].Value + newline : "") + match.Groups[1].Value + value);
            if (section.Length > 0 && !section.EndsWith("\n", StringComparison.Ordinal)) section += newline;
            return section + key + " = " + value + newline;
        }
    }

    internal static class Installation
    {
        private const string Language = @"Localization\CHN";
        private const string BackupFolder = "ZH-CN-Backup";

        internal static string Install(IList<Target> targets)
        {
            if (targets.Count == 0 || targets.Select(p => p.Root).Distinct(StringComparer.OrdinalIgnoreCase).Count() != targets.Count)
                throw new InvalidOperationException("请为要安装的程序选择各自的安装目录。");

            var locks = new List<FileStream>();
            var plans = new List<Plan>();
            var applied = new List<Replacement>();
            try
            {
                foreach (var t in targets.OrderBy(x => x.Root, StringComparer.OrdinalIgnoreCase))
                {
                    t.Product.RequireClosed();
                    locks.Add(new FileStream(Path.Combine(t.Root, ".zh-cn-install.lock"),
                        FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose));
                }

                using (var bundled = Assembly.GetExecutingAssembly().GetManifestResourceStream("ZhCnPayload.zip"))
                using (var zip = new ZipArchive(bundled, ZipArchiveMode.Read))
                {
                    foreach (var target in targets)
                    {
                        var plan = new Plan(target);
                        plans.Add(plan);
                        plan.Prepare(zip);
                    }
                }

                foreach (var t in targets) t.Product.RequireClosed();
                foreach (var plan in plans)
                foreach (var item in plan.Changes)
                {
                    applied.Add(item);
                    item.Apply();
                }

                return String.Join("\n\n", plans.Select(p => p.Summary()));
            }
            catch (Exception failure)
            {
                var restoreErrors = new List<string>();
                foreach (var item in Enumerable.Reverse(applied))
                {
                    try { item.Undo(); }
                    catch (Exception ex) { restoreErrors.Add(item.Backup + "\n" + ex.Message); }
                }
                if (restoreErrors.Count > 0)
                    throw new IOException("自动恢复遇到文件占用或权限问题。原文件备份位置\n" +
                        String.Join("\n", restoreErrors), failure);
                throw;
            }
            finally
            {
                foreach (var plan in plans) plan.CleanStaging();
                foreach (var file in locks) file.Dispose();
            }
        }

        private static void CheckForLink(string path)
        {
            if ((Directory.Exists(path) || File.Exists(path)) &&
                (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("请使用真实的安装目录。检测到目录联接或链接\n" + path);
        }

        private sealed class Plan
        {
            private readonly Target target;
            private readonly string backup, stage;
            internal readonly List<Replacement> Changes = new List<Replacement>();

            internal Plan(Target selected)
            {
                target = selected;
                var parent = Path.Combine(target.Root, BackupFolder);
                CheckForLink(parent);
                backup = Path.Combine(parent, DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" +
                    Guid.NewGuid().ToString("N").Substring(0, 6));
                stage = Path.Combine(backup, ".staging");
            }

            internal void Prepare(ZipArchive zip)
            {
                string skin = @"Skins\" + target.Product.Skin;
                foreach (var path in new[] { Language, skin, target.Product.Config })
                {
                    CheckForLink(Path.Combine(target.Root, Path.GetDirectoryName(path)));
                    CheckForLink(Path.Combine(target.Root, path));
                }

                string prefix = target.Product.Id + "/";
                string langPrefix = Language.Replace('\\', '/') + "/";
                foreach (var entry in zip.Entries.Where(e => e.FullName.StartsWith(prefix, StringComparison.Ordinal)))
                {
                    string key = entry.FullName.Substring(prefix.Length);
                    if (!key.StartsWith(langPrefix, StringComparison.Ordinal) && key != skin.Replace('\\', '/'))
                        throw new InvalidDataException("安装包有无效的资源路径。");
                    if (key.Split('/').Any(a => a.Length == 0 || a == "." || a == ".." ||
                        a.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0))
                        throw new InvalidDataException("安装包有无效的文件名。");
                    string file = Path.Combine(stage, key.Replace('/', '\\'));
                    Directory.CreateDirectory(Path.GetDirectoryName(file));
                    using (var input = entry.Open())
                    using (var output = new FileStream(file, FileMode.CreateNew)) input.CopyTo(output);
                }

                if (!File.Exists(Path.Combine(stage, Language, "Description")) || !File.Exists(Path.Combine(stage, skin)))
                    throw new InvalidDataException("安装包缺少 " + target.Product.Name + " 的语言包或皮肤。");

                var config = Path.Combine(target.Root, target.Product.Config);
                var stagedConfig = Path.Combine(stage, target.Product.Config);
                Directory.CreateDirectory(Path.GetDirectoryName(stagedConfig));
                File.WriteAllBytes(stagedConfig, SettingsFile.SelectChinese(
                    File.Exists(config) ? File.ReadAllBytes(config) : new byte[0], target.Product.Skin));

                foreach (var rel in new[] { Language, skin, target.Product.Config })
                {
                    var item = new Replacement(Path.Combine(target.Root, rel),
                        Path.Combine(stage, rel), Path.Combine(backup, rel), rel == Language);
                    if (item.IsDifferent()) Changes.Add(item);
                }
            }

            internal string Summary()
            {
                if (Changes.Count == 0) return target.Product.Name + " 已安装此版本的汉化。";
                var message = target.Product.Name + " 已启用简体中文和中文皮肤。";
                return Changes.Any(item => item.HasBackup) ? message + "\n备份 " + backup : message;
            }

            internal void CleanStaging()
            {
                try
                {
                    if (Directory.Exists(stage)) Directory.Delete(stage, true);
                    if (Directory.Exists(backup) && Changes.All(item => !item.HasBackup))
                        Directory.Delete(backup, true);
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }

        private sealed class Replacement
        {
            private readonly string live, staged;
            internal readonly string Backup;
            private readonly bool folder;
            private bool placed;
            internal bool HasBackup { get; private set; }

            internal Replacement(string live, string staged, string backup, bool folder)
            {
                this.live = live;
                this.staged = staged;
                Backup = backup;
                this.folder = folder;
            }

            internal bool IsDifferent()
            {
                if (!folder) return !EqualFiles(live, staged);
                if (!Directory.Exists(live)) return true;
                return !EqualDirectories(live, staged);
            }

            private static bool EqualFiles(string a, string b)
            {
                CheckForLink(a);
                var old = new FileInfo(a);
                return old.Exists && old.Length == new FileInfo(b).Length &&
                    File.ReadAllBytes(a).SequenceEqual(File.ReadAllBytes(b));
            }

            private static bool EqualDirectories(string a, string b)
            {
                CheckForLink(a);
                string[] old = Directory.GetFileSystemEntries(a);
                string[] next = Directory.GetFileSystemEntries(b);
                if (old.Length != next.Length) return false;
                foreach (var item in next)
                {
                    string prior = Path.Combine(a, Path.GetFileName(item));
                    if (Directory.Exists(item))
                    {
                        if (!Directory.Exists(prior) || !EqualDirectories(prior, item)) return false;
                    }
                    else if (!EqualFiles(prior, item)) return false;
                }
                return true;
            }

            private void Move(string from, string to)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(to));
                if (folder) Directory.Move(from, to);
                else File.Move(from, to);
            }

            internal void Apply()
            {
                if (!folder && File.Exists(live))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(Backup));
                    try
                    {
                        // ReplaceFile preserves the destination's ACL and metadata.
                        File.Replace(staged, live, Backup);
                        placed = true;
                    }
                    finally
                    {
                        // A failed replacement can still have renamed the original.
                        HasBackup = File.Exists(Backup);
                    }
                    return;
                }

                if (File.Exists(live) || Directory.Exists(live))
                {
                    Move(live, Backup);
                    HasBackup = true;
                }
                Move(staged, live);
                placed = true;
            }

            internal void Undo()
            {
                // The original lives in the backup until the whole install is committed.
                if (placed)
                {
                    Move(live, staged);
                    placed = false;
                }
                if (HasBackup)
                {
                    Move(Backup, live);
                    HasBackup = false;
                }
            }
        }
    }
}
