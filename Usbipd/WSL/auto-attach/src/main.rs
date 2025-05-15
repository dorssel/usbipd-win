// SPDX-FileCopyrightText: 2022 Frans van Dorsselaer (original shell script)
// SPDX-License-Identifier: GPL-3.0-only

use std::env;
use std::fs;
use std::io;
use std::path::Path;
use std::process::Command;
use std::thread;
use std::time::Duration;
use clap::Parser;

/// A macro for conditional logging in verbose mode
macro_rules! verbose_log {
    ($verbose:expr, $($arg:tt)*) => {
        if $verbose {
            eprintln!($($arg)*);
        }
    };
}

struct AttachState {
    is_attached: bool,
    last_error: String,
    last_reported_error: String,
}

fn report_attached(state: &mut AttachState, attached: bool) {
    let old_attached = state.is_attached;
    state.is_attached = attached;

    if state.is_attached != old_attached {
        if state.is_attached {
            println!("Attached");
        } else {
            println!("Detached");
        }
        state.last_reported_error = String::new();
    }

    if !state.is_attached && state.last_reported_error != state.last_error {
        println!("{}", state.last_error);
        state.last_reported_error = state.last_error.clone();
    }
}

fn try_attach(host: &str, busid: &str, verbose: bool) -> Result<String, String> {
    // Using the current executable's directory to find usbip
    let current_exe = match env::current_exe() {
        Ok(path) => path,
        Err(e) => {
            verbose_log!(verbose, "Error finding current executable path: {}", e);
            return Err(format!("Current executable error: {}", e));
        }
    };
    
    let exe_dir = match current_exe.parent() {
        Some(dir) => dir,
        None => {
            verbose_log!(verbose, "Could not determine executable directory from: {:?}", current_exe);
            return Err("Could not determine executable directory".to_string());
        }
    };
    
    let usbip_path = exe_dir.join("usbip");
    verbose_log!(verbose, "Looking for usbip at: {:?}", usbip_path);
    
    if !usbip_path.exists() {
        verbose_log!(verbose, "usbip binary not found at: {:?}", usbip_path);
        return Err(format!("usbip binary not found at: {:?}", usbip_path));
    }

    verbose_log!(verbose, "Executing: {:?} attach --remote {} --busid {}", usbip_path, host, busid);
    let output = match Command::new(&usbip_path)
        .args(&["attach", "--remote", host, "--busid", busid])
        .output() {
            Ok(output) => output,
            Err(e) => {
                verbose_log!(verbose, "Failed to execute usbip command: {}", e);
                return Err(format!("Command execution failed: {}", e));
            }
        };

    if !output.status.success() {
        let stderr = String::from_utf8_lossy(&output.stderr).to_string();
        let stdout = String::from_utf8_lossy(&output.stdout).to_string();
        verbose_log!(verbose, "usbip command failed with stderr: {}", stderr);
        verbose_log!(verbose, "usbip command stdout: {}", stdout);
        return Err(stderr);
    }

    Ok(String::new())
}

fn is_attached(host: &str, busid: &str, verbose: bool) -> io::Result<bool> {
    let status_path = "/sys/devices/platform/vhci_hcd.0/status";
    verbose_log!(verbose, "Checking vhci_hcd status at: {}", status_path);
    
    // Check if the status file exists
    if !Path::new(status_path).exists() {
        verbose_log!(verbose, "Status file not found: {}", status_path);
        return Err(io::Error::new(io::ErrorKind::NotFound, 
                                  format!("Status file not found: {}", status_path)));
    }
    
    // Read and parse vhci_hcd status
    let status_content = match fs::read_to_string(status_path) {
        Ok(content) => content,
        Err(e) => {
            verbose_log!(verbose, "Failed to read status file {}: {}", status_path, e);
            return Err(e);
        }
    };
    
    let mut lines = status_content.lines();

    // Skip header line
    // Expected format:
    // hub port sta spd dev      sockfd local_busid
    // hs  0000 006 002 00040002 000003 1-1
    // hs  0001 004 000 00000000 000000 0-0
    lines.next();

    // Process each device line
    for line in lines {
        let parts: Vec<&str> = line.split_whitespace().collect();
        if parts.len() < 6 {
            verbose_log!(verbose, "Skipping malformed status line: {}", line);
            continue;
        }

        // Parse sockfd in base 10, exactly as in bash: local SOCKFD=$((10#${strarr[5]}))
        let sockfd = match parts[5].parse::<i32>() {
            Ok(val) => val,
            Err(e) => {
                verbose_log!(verbose, "Failed to parse sockfd '{}': {}", parts[5], e);
                continue;
            }
        };
        
        // Just like bash script: if ((SOCKFD == 0)); then continue; fi
        if sockfd == 0 {
            // No device on this port
            continue;
        }

        // Parse port number in base 10, exactly as in bash: local PORT=$((10#${strarr[1]}))
        let port = match parts[1].parse::<u32>() {
            Ok(val) => val,
            Err(e) => {
                verbose_log!(verbose, "Failed to parse port '{}': {}", parts[1], e);
                continue;
            }
        };
        
        let port_file = format!("/var/run/vhci_hcd/port{}", port);
        verbose_log!(verbose, "Checking port file: {}", port_file);

        // Use metadata to check if file exists and handle permission errors
        match Path::new(&port_file).metadata() {
            Ok(_) => {}, // File exists and is accessible
            Err(e) => {
                if e.kind() == io::ErrorKind::NotFound {
                    verbose_log!(verbose, "Port file not found: {}", port_file);
                } else if e.kind() == io::ErrorKind::PermissionDenied {
                    verbose_log!(verbose, "Permission denied accessing port file: {}", port_file);
                } else {
                    verbose_log!(verbose, "Error checking port file: {} - {}", port_file, e);
                }
                continue;
            }
        }

        // Read port file content
        // Expected format:
        // 172.21.0.1 3240 4-2
        let port_content = match fs::read_to_string(&port_file) {
            Ok(content) => content,
            Err(e) => {
                verbose_log!(verbose, "Failed to read port file {}: {}", port_file, e);
                continue;
            }
        };
        
        verbose_log!(verbose, "Port file content: {}", port_content);
        let port_parts: Vec<&str> = port_content.split_whitespace().collect();
        
        if port_parts.len() >= 3 {
            let remote_ip = port_parts[0];
            let remote_busid = port_parts[2];
            verbose_log!(verbose, "Found device - IP: {}, BUSID: {}", remote_ip, remote_busid);

            if remote_ip == host && remote_busid == busid {
                verbose_log!(verbose, "Found matching device!");
                return Ok(true);
            }
        } else {
            verbose_log!(verbose, "Invalid port file format: {}", port_content);
        }
    }

    verbose_log!(verbose, "No matching device found");
    Ok(false)
}

fn safe_sleep(seconds: u64, verbose: bool) {
    verbose_log!(verbose, "Sleeping for {} seconds", seconds);
    thread::sleep(Duration::from_secs(seconds));
}

// Time between checks in seconds
const CHECK_INTERVAL_SECONDS: u64 = 1;

/// USB/IP auto-attach utility for Windows Subsystem for Linux
#[derive(Parser, Debug)]
#[clap(author, version, about, long_about = None)]
struct Args {
    /// Host IP address where the USB device is attached
    #[clap(required = true)]
    host: String,

    /// Bus ID of the USB device to attach
    #[clap(required = true)]
    busid: String,

    /// Enable verbose logging
    #[clap(short, long)]
    verbose: bool,
}

fn main() {
    // Parse command line arguments using clap
    let args = Args::parse();
    
    let verbose = args.verbose;
    let host = &args.host;
    let busid = &args.busid;
    
    verbose_log!(verbose, "Starting auto-attach with host={}, busid={}", host, busid);
    
    let mut state = AttachState {
        is_attached: false,
        last_error: String::new(),
        last_reported_error: String::new(),
    };

    loop {
        verbose_log!(verbose, "Checking if device is attached...");
        match is_attached(host, busid, verbose) {
            Ok(true) => {
                verbose_log!(verbose, "Device is attached");
                report_attached(&mut state, true);
            },
            Ok(false) => {
                verbose_log!(verbose, "Device is not attached");
                report_attached(&mut state, false);
                
                // Always try to attach when the device is not found, like in the bash script
                verbose_log!(verbose, "Attempting to attach device");
                match try_attach(host, busid, verbose) {
                    Ok(_) => {
                        verbose_log!(verbose, "Attachment successful");
                        state.last_error = String::new();
                        report_attached(&mut state, true);
                    },
                    Err(error) => {
                        verbose_log!(verbose, "Attachment failed: {}", error);
                        state.last_error = error;
                        report_attached(&mut state, false);
                    }
                }
            },
            Err(e) => {
                verbose_log!(verbose, "Error checking attachment status: {}", e);
                state.last_error = e.to_string();
                report_attached(&mut state, false);
            }
        }
        
        safe_sleep(CHECK_INTERVAL_SECONDS, verbose);
    }
}
