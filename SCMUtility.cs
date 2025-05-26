// Copyright (c) iQubit Inc. All rights reserved.
// Use of this source code is governed by a GPL-v3 license

namespace SCMApp {
  using System;
  using LibGit2Sharp;

  internal class GitUtility {
    /// <summary>
    /// Action representing sequence of git commands
    /// <see href="https://stackoverflow.com/q/105372">
    /// enumeration reference: SO - How to enumerate an enum
    /// </see>
    /// </summary>
    public enum SCMAction {
      Push,   // push modified / push single / push all
      UpdateRemote,
      Branch,
      ShowInfo,
      ShowStatus,
      Pull,
      Rebase
    };

    /// <summary>
    /// Repository Instance
    /// </summary>
    private Repository Repo { get; set; }
    /// <summary>
    /// What Git Action to perform
    /// </summary>
    private SCMAction Action { get; set; }
    /// <summary>
    /// Configuration Instance
    /// </summary>
    private JsonConfig Config { get; set; }
    /// <summary>
    /// Whether repository is in init stage
    /// </summary>
    private bool RepoInitStage { get; set; }

    public GitUtility(SCMAction action, string repoPath, string jsonConfigurationFilePath) {
      if (string.IsNullOrEmpty(repoPath))
        repoPath = System.IO.Directory.GetCurrentDirectory();
      else if (! System.IO.Directory.Exists(repoPath))
        throw new ArgumentException("Provided repository directory path does not exist!");

      try {
        Repo = new Repository(repoPath);
      }
      catch (RepositoryNotFoundException e) {
        Console.WriteLine("Repo dir: " + repoPath + " " + e.Message);

        Console.WriteLine("Initialize a repository in this location: " + repoPath + "?");
        var response = Console.ReadLine();
        if (string.IsNullOrEmpty(response) || response[0] != 'Y' && response[0] != 'y')
          throw ;
        
        // ref, LibGit2Sharp.Tests/PushFixture.cs
        var res = Repository.Init(repoPath, isBare: false);
        // Console.WriteLine("result: " + res);

        Repo = new Repository(repoPath);
        RepoInitStage = true;
        // Cannot do branches Add here since we don't have the commit or commit SHA here..
        // branch = Repo.Branches.Add("dev", commit);        
      }
      // catch any other exception thrown
      catch (LibGit2SharpException e) {
          Console.WriteLine(e.Message);
          throw e;
      }
 
      Action = action;
      Config = new JsonConfig(jsonConfigurationFilePath, Repo.Config.Get<string>("user.name").Value, Repo.Config.
        Get<string>("user.email").Value, GetRepoPath());
    }

    /// <summary>
    /// Get short commit sha id: 9 alpha digits
    /// Truncate the tip of the Head
    /// </summary>
    private string GetShaShort() {
      if (Repo.Head.Tip == null) {
        Console.WriteLine("Head doesn't exist yet! Yet to do a first commit and create branch?");
        return string.Empty;
      }

      return Repo.Head.Tip.Id.ToString().Substring(0, 9); 
    }


    /// <summary>
    /// Get rid of the suffix from git repo path
    /// <example>
    /// Otherwise output string comes as following,
    ///  "D:\Code\CoolApp\.git\"
    /// After trimming suffix it becomes,
    ///  "D:\Code\CoolApp"
    /// </example>
    /// </summary>
    private string GetRepoPath() {
      var repoPath = Repo.Info.WorkingDirectory;
      if (repoPath == null)
        throw new NotImplementedException($"Most likely a bare repository: {Repo.Info.Path}. It is" + 
        " not tested with this app yet.");

      return repoPath.Substring(0, repoPath.Length-1);
    }


    /// <summary>
    /// Show git config info and show all remotes
    /// </summary>
    public void ShowRepoAndUserInfo() {
      Console.WriteLine("Local Repo: " + GetRepoPath());
      // or, GetValueOrDefault<string>
      var username = Repo.Config.Get<string>("user.name").Value;
      Console.WriteLine("Author: " + username);
      var email = Repo.Config.Get<string>("user.email").Value;
      Console.WriteLine("Email: " + email);
      Console.WriteLine("Branch: " + Repo.Head);
      Console.WriteLine("SHA: " + GetShaShort());

      Console.WriteLine(Environment.NewLine + "Global Config:");
      // ref: `LibGit2Sharp/Configuration.cs`
      if (Repo.Config.HasConfig(ConfigurationLevel.Global)) {
        username = Repo.Config.Get<string>("user.name", ConfigurationLevel.Global).Value;
        Console.WriteLine("Author: " + username);
        email = Repo.Config.Get<string>("user.email", ConfigurationLevel.Global).Value;
        Console.WriteLine("Email: " + email);
      }

      // ref: 'LibGit2Sharp.Tests/RemoteFixture.cs'
      Console.WriteLine();

      int count = 0;
      foreach (Remote remote in Repo.Network.Remotes)
          count++;
      Console.WriteLine(count + " remotes found.");

      foreach (Remote remote in Repo.Network.Remotes)
      {
          Console.WriteLine(remote.Name);
          Console.WriteLine(" URL: " + remote.Url);
          Console.WriteLine(" Push Url: " + remote.PushUrl);
      }
    }

    public void ShowStatus() {
      ShowRepoAndUserInfo();

      var statusOps = new StatusOptions();
      statusOps.IncludeIgnored = false;

      Console.WriteLine(Environment.NewLine + "Local changes:");
      foreach (var item in Repo.RetrieveStatus(statusOps))
        Console.WriteLine((item.State == FileStatus.ModifiedInWorkdir? "*": " ") + " " + item.FilePath);

      Console.WriteLine(Environment.NewLine + "Message (to be used with next commit):");
      Console.WriteLine(GetCommitMessage(singleLine: true));
      Console.WriteLine("..." + Environment.NewLine);
    }

    /// <summary>
    /// Get commit message from commit log file (full or first line of it)
    /// <see href="https://stackoverflow.com/q/52598516">SO - Read first line of a text file C#</see>
    /// </summary>
    /// <param name="singleLine">whether to return only first line</param>
    /// <returns>retrieved message</returns>
    private string GetCommitMessage(bool singleLine = false) {
      var commitFilePath = Config.GetCommitFilePath();

      if (!System.IO.File.Exists(commitFilePath))
        throw new InvalidOperationException($"Log file: {commitFilePath} not found!");

      if (singleLine)
        // Open the file to read from
        using (System.IO.StreamReader sr = System.IO.File.OpenText(commitFilePath)) {
            return sr.ReadLine() ?? string.Empty;
        }
      else
        return System.IO.File.ReadAllText(commitFilePath);
    }

    /// <summary>
    /// Pull changes from remote
    /// </summary>
    /// <remarks>
    /// ref, Test: NetworkFixture.cs
    /// does not support pull from private repository
    ///  TODO: test with private
    /// </remarks>
    public void PullChanges(bool isRemoteUpstream) {
      // will be rquired if repository requires authentication
      // InstantiateJsonConfig();
      var fetchOptions = new PullOptions() {
        FetchOptions = new FetchOptions() /*{
          CredentialsProvider = Config.GetCredentials(),
        },*/
      };
      var options = new PullOptions() {
        MergeOptions = new MergeOptions() {
            FastForwardStrategy = FastForwardStrategy.Default
        }
      };

      var signature = Repo.Config.BuildSignature(DateTimeOffset.Now);
      // TODO: add an optional argument to provide this
      var remoteBranchName = "main";    // usually main for GitHub etc.
      var previousSha = GetShaShort();

      try {
        if (! isRemoteUpstream) {
          MergeResult mergeResult = Commands.Pull(Repo, signature, options);
          Console.WriteLine($"Merge result: {mergeResult.Status}");
          }
        else {
          var remoteName = "upstream";
          var refSpec = string.Format("refs/heads/{2}:refs/remotes/{0}/{1}", remoteName, Repo.Head.FriendlyName, remoteBranchName);
          Console.WriteLine($"pulling {remoteName}/{remoteBranchName}");

          // Perform the actual fetch
          Commands.Fetch(Repo, remoteName, new string[] { refSpec },
            fetchOptions.FetchOptions,
            null
          );
          // Merge fetched refs
          MergeResult mergeResult = Repo.MergeFetchedRefs(signature, fetchOptions.MergeOptions);
          Console.WriteLine($"Merge result: {mergeResult.Status}");
        }
      }
      catch (CheckoutConflictException e) {
          Console.WriteLine("Conflict: " + e.Message);
          return ;
      }
      catch (LibGit2Sharp.MergeFetchHeadNotFoundException e) {
        Console.WriteLine($"{remoteBranchName} does not exist! " + e.Message);
        return ;
      }
      // catch any other exception thrown
      catch (LibGit2SharpException e) {
          Console.WriteLine(e.Message);
          return ;
      }

      Console.WriteLine($"{Repo.Head}: {previousSha} -> {GetShaShort()}");
    }

    /// <summary>
    /// At present 3 types are supported
    /// - single: only specified file
    /// - Update: indicates only modified files
    /// - all files in the repository workspace
    /// </summary>
    public enum StageType {
      Single,
      Update,
      All
    };


    /// <summary>
    /// Stage changes for committing
    /// </summary>
    /// <remarks>
    /// Provides hardcoded relative path support for 'input\posts'
    /// </remarks>
    /// <param name="stageType"><see cref="StageType"/></param>
    /// <param name="filePath">file path passed with 'push single'</param>
    /// <returns>indicates whether any change was staged</returns>
    private bool Stage(StageType stageType, string filePath) {
      bool isModified = false;
      var statusOps = new StatusOptions{IncludeIgnored = false};

      switch(stageType) {
      case StageType.Single:
        if (string.IsNullOrEmpty(filePath) == false)
        {
          var repoPath = GetRepoPath();  
          // Get relative path of the dir
          if (filePath.StartsWith(repoPath))
            filePath = filePath.Substring(repoPath.Length + 1);

          var statiqPostsPath = @"input\posts";
          if (System.IO.Directory.Exists(statiqPostsPath) && filePath.EndsWith(".md") && !filePath
              .StartsWith(statiqPostsPath)) {
            var newFilePath = statiqPostsPath + '\\' + filePath;
            if (System.IO.File.Exists(newFilePath))
              filePath = newFilePath;
          }
        }

        if (System.IO.Directory.Exists(filePath)) {
          var dirPath = filePath;
          Console.WriteLine("d " + filePath);

          string[] fileEntries = System.IO.Directory.GetFiles(dirPath);

          // Stage all files found inside the directory, TODO: recurse
          foreach (string fileName in fileEntries) {
             var sPath = fileName.Substring(dirPath.Length + 1);
            Console.WriteLine(" * " + sPath);

            Repo.Index.Add(fileName);

            if (!isModified)
              isModified = true;
          }
        }
        else if (System.IO.File.Exists(filePath)) {
          Console.WriteLine("* " + filePath);

          Repo.Index.Add(filePath);
          isModified = true;
        }
        else // Logger Verbose
          Console.WriteLine($"{filePath} doesn't exist!");
        break;

      case StageType.Update:
        foreach (var item in Repo.RetrieveStatus(statusOps)) {
            // Stage file if it's modified
            if (item.State == FileStatus.ModifiedInWorkdir && System.IO.File.Exists(item.FilePath))
            {
              Console.WriteLine("* " + item.FilePath);
              Repo.Index.Add(item.FilePath);

              if (!isModified)
                isModified = true;
            }
        }
        break;
      
      case StageType.All:
        foreach (var item in Repo.RetrieveStatus(statusOps)) {
          // Stage any file found
          if (System.IO.File.Exists(item.FilePath)) {
            Console.WriteLine("* " + item.FilePath);
            Repo.Index.Add(item.FilePath);

            if (!isModified)
              isModified = true;
          }
        }
        break;

      default:
        break;
      }

      if (isModified)
            Repo.Index.Write();

      return isModified;
    }

    /// <summary>
    /// Commit staged changes
    /// - create the committer's signature
    /// - use that signature to commit
    /// * Get Signature from Repo Config ref,
    /// * And, Amend ref,
    /// <see href="LibGit2Sharp.Tests/CommitFixture.cs">LibGit2Sharp CommitFixture Tests</see>
    /// </summary>
    private void Commit(bool shouldAmend = false) {
      Console.WriteLine("Current HEAD at: " + GetShaShort() + (shouldAmend? " (being rewritten!)":
        string.Empty));

      Signature signature = Repo.Config.BuildSignature(DateTimeOffset.Now);

      // Commit to local repository
      Console.WriteLine("Commit author name: " + signature.Name);
      Console.WriteLine("Commit author email: " + signature.Email);

      Commit commit = Repo.Commit(GetCommitMessage(), signature, signature,
        new CommitOptions { AmendPreviousCommit = shouldAmend });

      // TODO: Use Logger Verbose to display addition inf
      Console.WriteLine("and message:");
      Console.WriteLine(' ' + GetCommitMessage(singleLine: true));
      Console.WriteLine("..." + Environment.NewLine);

      // Test this with an initialized repo (no commits)
      //  ref, https://github.com/libgit2/libgit2sharp/issues/802
      var branches = new EnumerableType<Branch>(Repo.Branches.GetEnumerator());
      var branch = branches.First();
      
      if (RepoInitStage) {
        Console.WriteLine("Branch init for LibGit2Sharp!");
        // should trigger right after a 'git init'
        // doesn't apply to LibGit2Sharp's Init which creates a master branch
        // branch = Repo.Branches.Add("dev", commit);
        branch = Repo.Branches.Rename("master", "dev");
        // Update the HEAD reference to point to the latest commit
        Repo.Refs.UpdateTarget(Repo.Refs.Head, branch.CanonicalName);

      }
      else if (branch == null) {
        Console.WriteLine("Branch init for git!");
        // should trigger right after a 'git init', smae comment? Hence, need to test
        // doesn't apply to LibGit2Sharp's Init which creates a master branch
        branch = Repo.Branches.Add("dev", commit);
        // TODO: change it to set active branch
        // Update the HEAD reference to point to the latest commit
        Repo.Refs.UpdateTarget(Repo.Refs.Head, branch.CanonicalName);
      }
    }

    /// <summary>
    /// Generic class to support Generic Type to return First Item when an IEnumerable is
    /// provided
    /// </summary>
    private class EnumerableType<T> {
      private System.Collections.Generic.IEnumerator<T> Iter;

      public EnumerableType(System.Collections.Generic.IEnumerator<T> iter) {
        Iter = iter;
      }

      public T? First() {
        var first = default(T);
        
        if (Iter.MoveNext())
          first = Iter.Current;

        return first;
      }
    }

    private string GetCommitMessageFromFirst() {
      var commits = new EnumerableType<Commit>(Repo.Commits.GetEnumerator());
      var lastCommit = commits.First();
      return lastCommit?.Message ?? string.Empty;
    }

    private bool HasCommitLogChanged() {
      string rMsg = GetCommitMessageFromFirst();

      // Logger Verbose
      if (rMsg == string.Empty)
        Console.WriteLine("failed to retrieve commit message!");

      var lMsg = GetCommitMessage();

      // Logger Verbose
      // Console.WriteLine("comparison result: " + (rMsg.Trim() != lMsg.Trim()));
      return rMsg.Trim() != lMsg.Trim();
    }

    private void OnPushStatusError(PushStatusError pushStatusErrors) {
      Console.WriteLine(string.Format("Failed to update reference '{0}': {1}",
          pushStatusErrors.Reference, pushStatusErrors.Message));
    }

    /// <summary>
    /// Push commits to remote
    ///  does force push when --amend flag is present
    /// <remarks>
    /// For a brand new git repository just initialized with no head and no branch
    ///  set name of the branch to be created as 'dev'
    ///
    /// - push ref spec primary attribution,
    ///  <see href="https://git-scm.com/book/en/v2/Git-Internals-The-Refspec">Git Refspec</see>
    ///
    /// - pertinent to push ref spec used in Network.Push
    /// <see href="https://stackoverflow.com/q/47294514">
    ///  SO - libgit2sharp Git cannot push non-fastforwardable reference
    /// </see>
    /// </remarks>
    /// </summary>
    private void PushToRemote(bool shouldForce = false) {
      if (RepoInitStage) {
        Console.WriteLine("Repo initialized. Please set remote origin and push again.");
        return ;
      }

      var currentBranch = GetCurrentBranch();
      // Use Logger Verbose
      var originBranchStr = "origin/" + currentBranch?.FriendlyName;

      // Example output, Q: What's counting?
      // Counting 1 0
      // Deltafying 0 3
      // Deltafying 3 3
      LibGit2Sharp.Handlers.PackBuilderProgressHandler packBuilderCb = (x, y, z) => {
        if (z == 0)
          Console.Write($" {x} 0%\r");
        else
          Console.Write($" {x} {y * 100 / z}%\r");

        return true;
      };

      var options = new PushOptions() {
          CredentialsProvider = Config.GetCredentials(),
          OnPushStatusError = OnPushStatusError,
          OnPackBuilderProgress = packBuilderCb
        };

      try {
        var formatSpec = (shouldForce || Repo.Branches[originBranchStr] == null)? "+{0}:{0}" : "{0}";
        var pushRefSpec = string.Format(formatSpec, Repo.Head.CanonicalName);

        var remote = Repo.Network.Remotes["origin"];
        if (remote == null) {
          Console.WriteLine("Exception: Remote origin not found! Try running with set-url argument.");
          throw new LibGit2SharpException("Remote origin not found!");
        }

        Console.WriteLine("Push progress:");
        if (shouldForce) {
          Repo.Network.Push(remote, pushRefSpec, options);
          Console.WriteLine(Environment.NewLine);
          Console.Write("Pushed new remote branch (or rewritten an old branch): " + currentBranch?.FriendlyName);
        }
        else {
          Repo.Network.Push(currentBranch, options);
          Console.WriteLine(Environment.NewLine);
          Console.Write("Pushed branch: " + currentBranch?.FriendlyName);
        }
      }
      catch (System.NullReferenceException) {
        Console.WriteLine("Unexpected, since branch is not hardcoded!");
        return ;
      }
      catch (NonFastForwardException) {
        Console.WriteLine("Attempting fast forward with force flag: " + shouldForce +
          " failed! Consider passing --amend");
        return ;
      }
      catch(LibGit2SharpException e) {
        Console.WriteLine("Exception: {0}", e.Message + (e.InnerException != null ? " / " + e.InnerException.Message : ""));
        Console.WriteLine("Canonical name: " + currentBranch?.CanonicalName);

        var remote = Repo.Network.Remotes["origin"];
        Console.WriteLine("URL: " + remote.Url);
        Console.WriteLine("Push URL: " + remote.PushUrl);
        return ;
      }
      catch (Exception ex) {
          Console.WriteLine("Exception: {0}", ex.Message + (ex.InnerException != null ? " / " + ex.InnerException.Message : ""));
      }

      Console.WriteLine((shouldForce? " (forced) " : " ") + "-> " + GetShaShort() + '.');
    }

    /// <summary>
    /// SCP - Stage, Commit and Push Modified/All
    /// </summary>
    /// <param name="filePath">file path passed with 'push single', Empty otherwise</param>
    /// <param name="shouldAmend">amend commit and force push</param>
    public void SCPChanges(StageType pushType, bool shouldAmend = false) {
      var isMod = false;

      switch (pushType) {
      case StageType.Update:
        isMod = Stage(StageType.Update, string.Empty);
        break;
      case StageType.All:
        isMod = Stage(StageType.All, string.Empty);;
        break;
      default:
        throw new ArgumentException("Unexpected stage type!");
      }

      if (isMod)
        Console.WriteLine("Above changes are staged." + Environment.NewLine);

      if (isMod || (shouldAmend && HasCommitLogChanged()))
        Commit(shouldAmend);
        
      PushToRemote(shouldForce: shouldAmend);
    }

    /// <summary>
    /// SCP - Stage, Commit and Push Single File
    /// </summary>
    public void SCPSingleChange(string filePath, bool shouldAmend = false) {
      var isMod = Stage(StageType.Single, filePath);
      if (isMod)
        Console.WriteLine("changes staged");

      if (isMod || (shouldAmend && HasCommitLogChanged()))
        Commit(shouldAmend);

      PushToRemote(shouldForce: shouldAmend);
    }

    /// <summary>
    /// Add/update remote URL
    /// </summary>
    /// <param name="remoteName">remote name i.e., origin or upstream</param>
    /// <param name="remoteURL">remote's URL</param>
    public void UpdateRemoteURL(string remoteName, string remoteURL) {
      if (Repo.Network.Remotes[remoteName] == null)
        Repo.Network.Remotes.Add(remoteName, remoteURL);
      else if (Repo.Network.Remotes[remoteName].Url == remoteURL &&
        Repo.Network.Remotes[remoteName].PushUrl == remoteURL)
      {
        Console.WriteLine("Already set to provided URL!");
      }
      else {
        Repo.Network.Remotes.Update(remoteName, r => r.Url = remoteURL);
        Repo.Network.Remotes.Update(remoteName, r => r.PushUrl = remoteURL);
      }
    }

    /// <summary>
    /// Delete specified branch from local and remote
    ///  Use the Push Refspec (till API support for remote deletion is found)
    ///
    /// - ref for push ref spec,
    ///  <see href="https://git-scm.com/book/en/v2/Git-Internals-The-Refspec">Git Refspec</see>
    /// </summary>
    /// <param name="branchName">branch name</param>
    public void DeleteBranch(string branchName, bool deleteRemoteOnly = false) {

      Console.WriteLine($"Branch {branchName}");

      var formatSpecDelRBranch = ":{0}";
      var pushRefSpec = string.Format(formatSpecDelRBranch, "refs/heads/" + branchName);
      var options = new PushOptions() { CredentialsProvider = Config.GetCredentials() };

      Repo.Network.Push(Repo.Network.Remotes["origin"], pushRefSpec, options);
      Console.WriteLine("- removed from remote (NoOp if already removed)");

      if (deleteRemoteOnly == false) {
        if (Repo.Branches[branchName] == null) {
          Console.WriteLine($"Branch {branchName} does not exist!");
          return ;
        }
        Repo.Branches.Remove(branchName, false);
        Console.WriteLine("- removed from local");
      }
      else
        Console.WriteLine("- not removed from local");
    }


    /// <summary>
    /// Renames current branch to giveName
    ///  - If it's a detached HEAD, ignore it
    ///  Utilizes the Push Refspec to rename remote branch
    /// </summary>
    /// <param name="targetBranchName">branch name to rename to</param>
    public void RenameBranch(string targetBranchName) {
      if (Repo.Branches[targetBranchName] is not null) {
        Console.WriteLine($"Branch {targetBranchName} already exists!");
        return ;
      }

      var currentBranch = GetCurrentBranch();
      Console.WriteLine($"Rename branch {currentBranch?.FriendlyName} -> {targetBranchName}");
      // Renames local branch: can rename active local branch
      var newBranch = Repo.Branches.Rename(currentBranch, targetBranchName);
      // Create new remote branch with the new name (copy of old branch but with new name)
      PushToRemote(shouldForce: true);

      // Update tracked branch, otherwise push goes to old remote branch
      string trackedBranchName = "refs/remotes/origin/" + newBranch.FriendlyName;
      Repo.Branches.Update(newBranch,
                    b => b.TrackedBranch = trackedBranchName);

      Console.WriteLine();
      Console.Write("Renaming remote branch creates a new branch copying the old one. ");
      Console.Write("Please change active branch on remote to the branch with with the new name ");
      Console.WriteLine("i.e., on GitHub site.");
      Console.WriteLine("GitHub URL to set active default branch looks like:");
      Console.WriteLine("  https://github.com/user_name/repository_name/settings");
      Console.WriteLine();
      Console.WriteLine("Done with the change yet? Please press Y if affirmative.");

      string response = Console.ReadLine()?? string.Empty;
      if (response != "y" && response != "Y")
        return ;

      // delete the original branch from remote keeping only the new one with the new name
      DeleteBranch(currentBranch?.FriendlyName?? "PlaceHolderBranchNameToAvoidWarning", deleteRemoteOnly: true);
    }

    /// <summary>
    /// Rebase
    ///  - Related to RewriteHistory
    ///  Utilize a new class to separate out Linq consumers
    /// </summary>
    /// <param name="param-to-add">TODO</param>
    public void AmendAuthor(string name, string email) {
      var rewriter = new Rewriter();
      rewriter.AmendAuthor(name, email, Repo);
    }


    /// <summary>
    /// Get currently active branch
    ///  the branch that is set as repository head
    /// </summary>
    /// <returns>Single Active Branch Object</returns>
    public Branch? GetCurrentBranch() {
      if (Repo.Info.IsHeadDetached)
        throw new InvalidOperationException("Head is detached! Ignoring Branch operation and throwing instead..");

      int count = 0;
      Branch? currentBranch = null;
      foreach( var branch in Repo.Branches)
        if (branch.IsCurrentRepositoryHead) {
          count++;
          currentBranch = branch;
        }

      if (count > 1)
        throw new System.ArgumentException("Multiple branches point to Head. Please specify source branch name to rename to!");
      else if (count == 0)
        throw new InvalidOperationException("An active branch could not be found!");
      else if (currentBranch == null)
        throw new NullReferenceException("Unexpected null reference as current branch!");

      return currentBranch;
    }
  }
}
