#!/bin/bash

# Ensure rust has been installed as per: https://rustup.rs/

# This is for building on a linux / wsl system. It can also be be built on windows with:

rustup target add x86_64-unknown-linux-musl
rustup target add aarch64-unknown-linux-musl

cross &> /dev/null || cargo install cross

# Build for x64
cargo build --release --target x86_64-unknown-linux-musl
# Build for arm64
cross build --release --target aarch64-unknown-linux-musl

mv target/x86_64-unknown-linux-musl/release/auto-attach ../x64/
mv target/aarch64-unknown-linux-musl/release/auto-attach ../arm64/

# For building on windows instead:
# > cargo install cross
# > cross build --release --target x86_64-unknown-linux-musl
# > cross build --release --target aarch64-unknown-linux-musl

