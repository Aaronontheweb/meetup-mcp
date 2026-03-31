# Thin layer on top of the SDK-published .NET container image.
# Creates the token-store directory with correct ownership so that
# Docker named-volume mounts inherit app:app instead of root:root.
ARG BASE_IMAGE=aaronontheweb/meetup-mcp:latest
FROM ${BASE_IMAGE}

USER root
RUN mkdir -p /home/app/.meetup-mcp && chown app:app /home/app/.meetup-mcp
USER $APP_UID
