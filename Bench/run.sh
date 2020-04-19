#!/bin/bash
hogs=$(pgrep -if "(typora|firefox|chrome|chromium-browser|rider|resharper|msbuild|telegram|clion|discord|slack)")
echo Suspending $(echo $hogs | wc -w) procs before running BDN
[[ -z "$hogs" ]] || echo $hogs | xargs kill -STOP
dotnet run -c release -- "$@"
echo Resuming $(echo $hogs | wc -w) procs after running BDN
[[ -z "$hogs" ]] || echo $hogs | xargs kill -CONT 
