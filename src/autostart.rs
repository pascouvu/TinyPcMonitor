//! Thin wrapper around the `auto-launch` crate for "Start with Windows".

use auto_launch::{AutoLaunch, AutoLaunchBuilder};
use std::io;

/// Build the AutoLaunch handle pointing at the currently running .exe.
pub fn build() -> io::Result<AutoLaunch> {
    let exe = std::env::current_exe()
        .map_err(|e| io::Error::other(format!("current_exe: {e}")))?;
    let exe_str = exe
        .to_str()
        .ok_or_else(|| io::Error::other("exe path is not valid UTF-8"))?;

    AutoLaunchBuilder::new()
        .set_app_name("PC-Monitor")
        .set_app_path(exe_str)
        .build()
        .map_err(|e| io::Error::other(format!("auto_launch: {e}")))
}
