# Chango privacy policy

_Last updated: 26 October 2026_

Chango is a file utility for Windows that does all its work on your PC. In short: **your files never leave your PC, and Chango collects no personal data.** Its only network request is an update check, which you can turn off (see [Network access](#network-access)).

## What Chango does with your files

- Everything Chango does (converting, compressing, resizing, editing, reading text from images, zipping and so on) runs **on your PC**. Your files are never uploaded anywhere.
- Chango only touches the files you choose with the wheel. Results are saved as **new files next to the original**; the original is never changed.
- "Share" opens the Windows share window, and "Copy to" and "Move to" use folders you pick. Where your files go after that is your choice.

## What Chango watches

To notice the gesture (dragging a file, then tapping Shift), Chango watches the mouse buttons and the Shift key while it runs, and listens for its keyboard shortcut, Ctrl + Alt + Space (you can turn the shortcut off in Settings → Shortcuts). It doesn't record, store or send keystrokes, mouse movements or anything you type.

When you use the shortcut, Chango asks File Explorer which files are selected, so it knows what to work on.

## What Chango stores

- **Settings** in `%APPDATA%\Chango\settings.json`: your preferences, such as theme, shortcut and wheel layout.
- **Logs** in `%LOCALAPPDATA%\Chango\logs`: technical notes such as errors, used for troubleshooting. File names are included only if you turn that on in Settings → Advanced. Logs stay on your PC and are never sent anywhere; you can open their folder from Settings → Advanced.

Uninstalling Chango removes the app. You can delete the folders above to remove its settings and logs too.

## Network access

Chango never sends your files, their names or anything about you anywhere. The only network request it makes is the update check, and only in the version downloaded from GitHub:

- **What:** at most once every 6 hours while Chango is running, an anonymous request to GitHub's public list of Chango releases (`api.github.com/repos/tiwariramesh/Chango/releases`, or the public `releases.atom` feed if GitHub is busy) to see whether a newer version exists. The request says it comes from "Chango" and which version, and nothing else. Like any website, GitHub sees your IP address; [GitHub's privacy statement](https://docs.github.com/site-policy/privacy-policies/github-general-privacy-statement) covers what it does with it.
- **Updates:** a new version is downloaded from that release on GitHub and checked against its published SHA-256 checksum before it's installed. If you installed Chango with its installer, it installs the update when you're not using Chango; otherwise it asks first.
- **Turning it off:** Settings → About → **Check for updates automatically** stops the checks, and **Install updates automatically** makes Chango ask before installing. "Check now" checks only when you press it.

The Microsoft Store version makes no requests of its own: the Microsoft Store keeps it up to date.

Buttons such as **Website**, **Star on GitHub** and **Email** open your browser or mail app only when you click them.

Chango has no accounts, ads, analytics, tracking or telemetry, and it doesn't sell or share any data.

## Other apps Chango can use

Some conversions use apps already installed on your PC: Microsoft Word, Excel and PowerPoint, LibreOffice, or FFmpeg. Chango runs them on your PC, on the file you chose; nothing is sent anywhere. Windows' own built-in tools (for example its PDF reader and tar) are used the same way.

## Children

Chango collects no personal information from anyone, including children.

## Changes

If this policy changes, the new version will be published here with a new date.

## Contact

Questions? Email Ramesh Tiwari at ramestiwarii@gmail.com.
