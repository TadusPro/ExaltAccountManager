use base64::{engine::general_purpose, Engine as _};
use serde_json::{json, Value};
use std::fs;
use std::path::Path;
use tauri::{AppHandle, Manager};
use tauri_plugin_shell::ShellExt;

/// Refreshes the runtime asset cache. If the network is unavailable, an
/// existing self-extracted cache may still be used; bundled item assets are
/// never returned.
#[tauri::command]
pub async fn refresh_asset_cache(app: AppHandle, force: bool) -> Result<Value, String> {
    let cache_directory = app
        .path()
        .app_data_dir()
        .map_err(|error| format!("Could not resolve the EAM app-data directory: {error}"))?
        .join("assets");
    fs::create_dir_all(&cache_directory)
        .map_err(|error| format!("Could not create the EAM asset cache: {error}"))?;

    let manifest_path = cache_directory.join("manifest.json");
    let render_sheet_path = cache_directory.join("renders.png");
    let mut arguments = vec![
        "--data-dir".to_string(),
        cache_directory.to_string_lossy().into_owned(),
        "--manifest".to_string(),
        manifest_path.to_string_lossy().into_owned(),
        "--render-sheet".to_string(),
        render_sheet_path.to_string_lossy().into_owned(),
    ];
    if force {
        arguments.push("--force".to_string());
    }

    let sidecar = app.shell().sidecar("eam-asset-extractor");
    let output = match sidecar {
        Ok(command) => command.args(arguments).output().await,
        Err(error) => {
            return load_cached_manifest(&manifest_path, &render_sheet_path).or_else(|_| {
                Err(format!("The EAM asset extractor sidecar is unavailable: {error}"))
            });
        }
    };

    match output {
        Ok(output) if output.status.success() => {}
        Ok(output) => {
            let details = String::from_utf8_lossy(&output.stderr).trim().to_string();
            return load_cached_manifest(&manifest_path, &render_sheet_path).or_else(|_| {
                Err(if details.is_empty() {
                    "The EAM asset extractor failed without an error message.".to_string()
                } else {
                    details
                })
            });
        }
        Err(error) => {
            return load_cached_manifest(&manifest_path, &render_sheet_path).or_else(|_| {
                Err(format!("The EAM asset extractor could not run: {error}"))
            });
        }
    }

    load_cached_manifest(&manifest_path, &render_sheet_path)
}

fn load_cached_manifest(manifest_path: &Path, render_sheet_path: &Path) -> Result<Value, String> {
    let manifest_text = fs::read_to_string(manifest_path)
        .map_err(|error| format!("Could not read the generated EAM asset manifest: {error}"))?;
    let mut manifest: Value = serde_json::from_str(&manifest_text)
        .map_err(|error| format!("The generated EAM asset manifest is invalid: {error}"))?;
    let render_sheet = fs::read(render_sheet_path)
        .map_err(|error| format!("Could not read the generated EAM render sheet: {error}"))?;

    manifest["renderSheetDataUrl"] = json!(format!(
        "data:image/png;base64,{}",
        general_purpose::STANDARD.encode(render_sheet)
    ));
    Ok(manifest)
}
