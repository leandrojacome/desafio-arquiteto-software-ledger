#!/bin/bash
set -eu

port="${1:?porta}"
path="${2:?caminho}"

exec 3<>"/dev/tcp/127.0.0.1/${port}"
printf 'GET %s HTTP/1.0\r\nHost: localhost\r\nConnection: close\r\n\r\n' "${path}" >&3

IFS= read -r -t 3 status_line <&3 || exit 1

case "${status_line}" in
    *" 200 "*) exit 0 ;;
    *) exit 1 ;;
esac
