use base64::{engine::general_purpose, Engine as _};
use serde_json::{json, Value};
use std::fs;
use std::path::{Path, PathBuf};
use tauri::{AppHandle, Manager};
use tauri_plugin_shell::ShellExt;

/// Extracts EAM item assets from the installed Realm client. Realm Updater is
/// the only component responsible for downloading or repairing game files.
pub async fn refresh_asset_cache_from_game(
    app: AppHandle,
    force: bool,
    game_exe_path: String,
) -> Result<Value, String> {
    let cache_directory = app
        .path()
        .app_data_dir()
        .map_err(|error| format!("Could not resolve the EAM app-data directory: {error}"))?
        .join("assets");
    fs::create_dir_all(&cache_directory)
        .map_err(|error| format!("Could not create the EAM asset cache: {error}"))?;

    let manifest_path = cache_directory.join("manifest.json");
    let render_sheet_path = cache_directory.join("renders.png");
    let resources_assets_path = resolve_resources_assets_path(Path::new(&game_exe_path));

    if !resources_assets_path.is_file() {
        let error = format!(
            "The installed Realm assets were not found at {}. Run Realm Updater first.",
            resources_assets_path.display()
        );
        return use_cached_manifest_or_error(force, &manifest_path, &render_sheet_path, error);
    }

    let mut arguments = vec![
        "--resources-assets".to_string(),
        resources_assets_path.to_string_lossy().into_owned(),
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
            return use_cached_manifest_or_error(
                force,
                &manifest_path,
                &render_sheet_path,
                format!("The EAM asset extractor sidecar is unavailable: {error}"),
            );
        }
    };

    match output {
        Ok(output) if output.status.success() => {}
        Ok(output) => {
            let details = String::from_utf8_lossy(&output.stderr).trim().to_string();
            return use_cached_manifest_or_error(
                force,
                &manifest_path,
                &render_sheet_path,
                if details.is_empty() {
                    "The EAM asset extractor failed without an error message.".to_string()
                } else {
                    details
                },
            );
        }
        Err(error) => {
            return use_cached_manifest_or_error(
                force,
                &manifest_path,
                &render_sheet_path,
                format!("The EAM asset extractor could not run: {error}"),
            );
        }
    }

    load_cached_manifest(&manifest_path, &render_sheet_path)
}

#[cfg(target_os = "windows")]
fn resolve_resources_assets_path(game_exe_path: &Path) -> PathBuf {
    game_exe_path
        .parent()
        .unwrap_or_else(|| Path::new(""))
        .join("RotMG Exalt_Data")
        .join("resources.assets")
}

#[cfg(target_os = "macos")]
fn resolve_resources_assets_path(game_exe_path: &Path) -> PathBuf {
    game_exe_path
        .join("Contents")
        .join("Resources")
        .join("Data")
        .join("resources.assets")
}

#[cfg(not(any(target_os = "windows", target_os = "macos")))]
fn resolve_resources_assets_path(game_exe_path: &Path) -> PathBuf {
    game_exe_path.join("resources.assets")
}

fn use_cached_manifest_or_error(
    force: bool,
    manifest_path: &Path,
    render_sheet_path: &Path,
    error: String,
) -> Result<Value, String> {
    if force {
        return Err(error);
    }

    load_cached_manifest(manifest_path, render_sheet_path).or(Err(error))
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
