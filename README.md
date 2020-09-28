# pwd
pwd is a simple console password manager.

Features:

- It is small, less than 400 lines of C# code.
- It is DRY, it is only focused on helping you managing passwords.
- It does not vendor-lock you, use pwd together with openssl and your text editor. 
- It is opensource, so you can see what it does (and it small).
- It is cross-platform, it works where dotnet works.
- It is a script, so add your functionality as you please.
- It is quite powerful (considering its size).

## Requirements

- (.NET core)[https://dotnet.microsoft.com/download]
- (dotnet-script)[https://github.com/filipw/dotnet-script]

## Quickstart

1. Install .NET core and dotnet-script
2. Clone/copy this repository
3. Run pwd.csx

## Story of pwd

For years I was in the search for the right password management tool. I've started
many, many years ago, with a plain text file. Internet wasn't a thing yet. OMG, even
networks were not a thing so plain text file wasn't actually a bad idea.

Over the years a few things happened:

- number of records in my passwords database had grown significally (like 1500 or something)
- I store not only passwords, but all different types of files as well (ssh keys, pdfs, etc)
- Internet had brought a whole set of new threats

So I'd evaluated a lot and tried some of the tools that claimed to help me managing my
passwords. Some of them really did actually and I've used these one for a while:
truecrypt, Password Commander, KeePass, 1Password.

Believe me, moving from one to another isn't fun. I wouldn't if I didn't have to. But
what are the alternatives if the application is just discontinued, or your dream job
is requires you to change an operating system? You cannot really let your software
make a live decisions for you, don't you agree?

Suffering enough, I came up with a list of requirements for the password management
software:

- It should be secure, obviously, but not just secure - understandably secure. Its
  security should be clear enough for me to understand.
- It should be portable, no installation required, just copy it around and it should work.
- It should be cross-platform, like really cross-platform: run on windows, linux, mac with
  the same user experience.
- It should have history of changes. Not immediatelly accessible history, but
  in case if something goes wrong it should be possible to track things backwards.
- It should be decentralised, I use a few windows and mac machines simultaneously and
  would like to makes changes and merge them.
- It should be programmable and flexible, and support different workflows.
- Its data format should be human readable and managable manually if needed be.

It was quite a challenge to find a tool that satisfies all these requirements.

But eventually I found a solution. It happened to be not a single tool, though.

I started using combination of vim encrypted text files with private git repository.
vim is a truly cross-platform text editor and works well on windows, linux and mac.
It has a blowfish encryption build-in, so security is covered. The passwords database
are just text files so I can just use `key: value` in them and it works like most of
the times. Small files, like keys, could be just base64-ed and stored in text form.
git covers backups, history, and merging and it also a cross-platform software.

Although vim encryption is pretty straightforward and it wasn't hard to create
decryptors for Go and node.js, I finally started using openssl's AES encryption,
so I can encrypt and decrypt any files from the command line, not only text files.
And I recently started using yaml as internal format for the files so the
programmability is a piece of cake.

./pwd.csx is an amalgamation of a few simple tools that I've developed while
automatising my daily password managing tasks. It can be used simultaneusly
with openssl to encrypt and decrypt files, its checks integrity of the passwords
database, and it allows to view several files without need to type the password
each time.

It is quite small, less then 400 lines. I want to keep it simple and understandable
so everyone can make sure that there are no hidden threats. The code is clean and
simple so new commands can be added easily.

This solution has been serving me well for a several years already. As it is now
a time-proven one, I'm happy to share it with you. Enjoy and be safe.