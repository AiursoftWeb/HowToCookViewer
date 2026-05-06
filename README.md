# HowToCook Viewer - Visualization and website of HowToCook

[![MIT licensed](https://img.shields.io/badge/license-MIT-blue.svg)](https://github.com/aiursoftweb/howToCookViewer/blob/master/LICENSE)
[![Pipeline stat](https://gitlab.aiursoft.com/aiursoft/howToCookViewer/badges/master/pipeline.svg)](https://gitlab.aiursoft.com/aiursoft/howToCookViewer/-/pipelines)
[![Test Coverage](https://gitlab.aiursoft.com/aiursoft/howToCookViewer/badges/master/coverage.svg)](https://gitlab.aiursoft.com/aiursoft/howToCookViewer/-/pipelines)
[![Man hours](https://manhours.aiursoft.com/r/github.com/aiursoftweb/howToCookViewer.svg)](https://manhours.aiursoft.com/r/github.com/aiursoftweb/howToCookViewer.html)
[![Website](https://img.shields.io/website?url=https%3A%2F%2Fhowtocook.aiursoft.com)](https://howtocook.aiursoft.com)
[![Docker](https://img.shields.io/docker/pulls/aiursoft/howtocookviewer.svg)](https://hub.docker.com/r/aiursoft/howtocookviewer)

HowToCookViewer is a sample project.

![screenshot](./screenshot.png)

Default user name is `admin@default.com` and default password is `Admin@123456!`.

## Try

Try a running HowToCookViewer [here](https://howtocook.aiursoft.com).

## Run in Ubuntu

The following script will install\update this app on your Ubuntu server. Supports Ubuntu 25.04.

On your Ubuntu server, run the following command:

```bash
curl -sL https://github.com/aiursoftweb/howToCookViewer/raw/master/install.sh | sudo bash
```

Of course it is suggested that append a custom port number to the command:

```bash
curl -sL https://github.com/aiursoftweb/howToCookViewer/raw/master/install.sh | sudo bash -s 8080
```

It will install the app as a systemd service, and start it automatically. Binary files will be located at `/opt/apps`. Service files will be located at `/etc/systemd/system`.

## Run manually

Requirements about how to run

1. Install [.NET 10 SDK](http://dot.net/) and [Node.js](https://nodejs.org/).
2. Execute `npm install` at `wwwroot` folder to install the dependencies.
3. Execute `dotnet run` to run the app.
4. Use your browser to view [http://localhost:5000](http://localhost:5000).

## Run in Microsoft Visual Studio

1. Open the `.sln` file in the project path.
2. Press `F5` to run the app.

## Run in Docker

First, install Docker [here](https://docs.docker.com/get-docker/).

Then run the following commands in a Linux shell:

```bash
image=aiursoft/howtocookviewer
appName=howtocookviewer
sudo docker pull $image
sudo docker run -d --name $appName --restart unless-stopped -p 5000:5000 -v /var/www/$appName:/data $image
```

That will start a web server at `http://localhost:5000` and you can test the app.

The docker image has the following context:

| Properties  | Value                           |
|-------------|---------------------------------|
| Image       | aiursoft/howtocookviewer        |
| Ports       | 5000                            |
| Binary path | /app                            |
| Data path   | /data                           |
| Config path | /data/appsettings.json          |

## How to contribute

There are many ways to contribute to the project: logging bugs, submitting pull requests, reporting issues, and creating suggestions.

Even if you with push rights on the repository, you should create a personal fork and create feature branches there when you need them. This keeps the main repository clean and your workflow cruft out of sight.

We're also interested in your feedback on the future of this project. You can submit a suggestion or feature request through the issue tracker. To make this process more effective, we're asking that these include more information to help define them more clearly.
