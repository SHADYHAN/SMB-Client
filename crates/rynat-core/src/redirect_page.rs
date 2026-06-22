use url::Url;

use crate::error::{CoreError, CoreResult};
use crate::link::{DEFAULT_PROTOCOL, QuickLinkTarget, build_deep_link};

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct RedirectPageOptions {
    pub app_name: String,
    pub retry_label: String,
    pub close_label: String,
    pub failed_title: String,
    pub failed_hint: String,
    pub install_hint: String,
}

impl Default for RedirectPageOptions {
    fn default() -> Self {
        Self {
            app_name: "RYNAT 共享网盘".to_string(),
            retry_label: "重试打开".to_string(),
            close_label: "关闭页面".to_string(),
            failed_title: "正在尝试打开 RYNAT 共享网盘".to_string(),
            failed_hint: "如果客户端没有自动打开，可以点击“重试打开”。".to_string(),
            install_hint: "如果多次重试仍无反应，请确认客户端已安装并联系 IT 管理员。".to_string(),
        }
    }
}

pub fn build_invisible_redirect_page(target: &QuickLinkTarget) -> CoreResult<String> {
    let deep_link = build_deep_link(DEFAULT_PROTOCOL, target)?;
    build_invisible_redirect_page_for_url(&deep_link, &RedirectPageOptions::default())
}

pub fn build_invisible_redirect_page_for_url(
    deep_link_url: &str,
    options: &RedirectPageOptions,
) -> CoreResult<String> {
    let url = Url::parse(deep_link_url)?;
    if url.scheme() != DEFAULT_PROTOCOL {
        return Err(CoreError::InvalidLink(format!(
            "redirect page can only open {DEFAULT_PROTOCOL}:// links"
        )));
    }

    let escaped_url = escape_html_attr(deep_link_url);
    let js_url = serde_json::to_string(deep_link_url).map_err(CoreError::from)?;
    let escaped_app = escape_html_text(&options.app_name);
    let retry = escape_html_text(&options.retry_label);
    let close = escape_html_text(&options.close_label);
    let failed_title = escape_html_text(&options.failed_title);
    let failed_hint = escape_html_text(&options.failed_hint);
    let install_hint = escape_html_text(&options.install_hint);

    Ok(format!(
        r#"<!doctype html>
<html lang="zh-CN">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1">
<title>{escaped_app}</title>
<style>
html,body{{margin:0;width:100%;height:100%;}}
body{{display:none;font-family:-apple-system,BlinkMacSystemFont,"Segoe UI","PingFang SC","Microsoft YaHei",sans-serif;background:#f6f7f9;color:#1f2933;}}
.fallback{{box-sizing:border-box;width:min(420px,calc(100vw - 48px));margin:auto;padding:28px;border:1px solid #d9dee7;background:#fff;box-shadow:0 18px 50px rgba(31,41,51,.14);}}
.mark{{width:42px;height:42px;margin-bottom:18px;background:#1f2933;color:#fff;display:grid;place-items:center;font-weight:700;}}
h1{{margin:0 0 8px;font-size:18px;line-height:1.35;font-weight:650;}}
p{{margin:0 0 18px;color:#667085;font-size:13px;line-height:1.6;}}
.actions{{display:flex;gap:10px;}}
a{{box-sizing:border-box;height:34px;padding:0 14px;display:inline-flex;align-items:center;justify-content:center;text-decoration:none;font-size:13px;border:1px solid #1f2933;color:#fff;background:#1f2933;}}
a.secondary{{color:#1f2933;background:#fff;}}
.hint{{margin-top:16px;margin-bottom:0;font-size:12px;color:#98a2b3;}}
</style>
</head>
<body>
<main class="fallback">
  <div class="mark">R</div>
  <h1>{failed_title}</h1>
  <p>{failed_hint}</p>
  <div class="actions">
    <a href="{escaped_url}">{retry}</a>
    <a class="secondary" href="javascript:window.close()">{close}</a>
  </div>
  <p class="hint">{install_hint}</p>
</main>
<script>
(function(){{
  var url = {js_url};
  var retried = false;
  var retry = document.querySelector("a");
  if (retry) {{
    retry.href = url;
    retry.addEventListener("click", function() {{
      retried = true;
      tryOpen();
    }});
  }}
  function closeTab(){{
    try {{ window.open("", "_self"); window.close(); }} catch (_) {{}}
  }}
  function tryOpen(){{
    try {{
      var frame = document.createElement("iframe");
      frame.style.display = "none";
      frame.src = url;
      document.body.appendChild(frame);
      setTimeout(function(){{ frame.remove(); }}, 800);
    }} catch (_) {{}}
    try {{ location.href = url; }} catch (_) {{}}
  }}
  setTimeout(tryOpen, 60);
  setTimeout(closeTab, 260);
  setTimeout(function(){{
    document.body.style.display = "flex";
    if (!retried && retry) retry.focus();
  }}, 1200);
  setTimeout(closeTab, 1800);
}})();
</script>
</body>
</html>"#
    ))
}

fn escape_html_text(input: &str) -> String {
    input
        .replace('&', "&amp;")
        .replace('<', "&lt;")
        .replace('>', "&gt;")
}

fn escape_html_attr(input: &str) -> String {
    escape_html_text(input).replace('"', "&quot;")
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::link::{LinkKind, QuickLinkTarget};

    #[test]
    fn redirect_page_is_hidden_until_launch_fails() {
        let target = QuickLinkTarget::new("nas", "Share", "/Docs", None, LinkKind::Directory);
        let html = build_invisible_redirect_page(&target).unwrap();

        assert!(html.contains("body{display:none"));
        assert!(html.contains("function tryOpen()"));
        assert!(html.contains("location.href = url;"));
        assert!(html.contains("frame.src = url;"));
        assert!(html.contains("setTimeout(closeTab, 260);"));
        assert!(html.contains("document.body.style.display = \"flex\""));
        assert!(html.contains("rynat://s?"));
        assert!(html.contains("var url = \"rynat://s?"));
        assert!(html.contains("href=\"rynat://s?h=nas&amp;s=Share"));
    }

    #[test]
    fn redirect_page_rejects_non_deep_links() {
        let error = build_invisible_redirect_page_for_url(
            "https://example.com/s?h=nas",
            &RedirectPageOptions::default(),
        )
        .unwrap_err();

        assert!(error.to_string().contains("rynat://"));
    }
}
