use serde::{Deserialize, Serialize};

use crate::link::{LinkOpenIntent, QuickLink, QuickLinkTarget};
use crate::preview::{PreviewPlan, PreviewRequest};
use crate::server::ServerProfile;
use crate::{CoreResult, CoreStore, parse_quick_link};

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct BrowseLocation {
    pub server_host: String,
    pub share: String,
    pub remote_path: String,
    pub selected_path: Option<String>,
}

impl From<LinkOpenIntent> for BrowseLocation {
    fn from(intent: LinkOpenIntent) -> Self {
        Self {
            server_host: intent.server_host,
            share: intent.share,
            remote_path: intent.browse_path,
            selected_path: intent.selected_path,
        }
    }
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct LinkActivation {
    pub target: QuickLinkTarget,
    pub matched_server: Option<ServerProfile>,
    pub browse_location: BrowseLocation,
    pub preview_plan: Option<PreviewPlan>,
}

impl LinkActivation {
    pub fn from_target(target: QuickLinkTarget, matched_server: Option<ServerProfile>) -> Self {
        let intent = target.open_intent();
        let preview_plan = intent.preview_path.as_ref().map(|_| {
            PreviewRequest::thumbnail(QuickLinkTarget::new(
                &target.server_host,
                &target.share,
                &target.path,
                target.name.clone(),
                target.kind,
            ))
            .plan()
        });

        Self {
            target,
            matched_server,
            browse_location: intent.into(),
            preview_plan,
        }
    }
}

#[derive(Clone)]
pub struct CoreSession {
    store: CoreStore,
}

impl CoreSession {
    pub fn new(store: CoreStore) -> Self {
        Self { store }
    }

    pub fn store(&self) -> &CoreStore {
        &self.store
    }

    pub fn activate_link(&self, raw_link: &str) -> CoreResult<LinkActivation> {
        let target = parse_quick_link(raw_link)?;
        self.activate_target(target)
    }

    pub fn activate_target(&self, target: QuickLinkTarget) -> CoreResult<LinkActivation> {
        let matched_server = self.store.find_server_profile_for_target(&target)?;
        Ok(LinkActivation::from_target(target, matched_server))
    }

    pub fn save_generated_link(&self, link: &QuickLink) -> CoreResult<()> {
        self.store.save_quick_link(link)
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::link::{LinkKind, QuickLinkTarget};
    use crate::preview::{PreviewContentType, PreviewKind};
    use crate::server::{AuthMode, ServerProfile};

    #[test]
    fn activates_file_link_with_server_match_and_preview() {
        let store = CoreStore::in_memory().unwrap();
        let profile = ServerProfile::new(
            "媒体 NAS",
            "nas.local",
            Some("alice".to_string()),
            AuthMode::UsernamePassword,
        )
        .unwrap();
        store.save_server_profile(&profile).unwrap();
        let session = CoreSession::new(store);

        let activation = session
            .activate_link("rynat://s?h=NAS.local&s=Media&p=/Movies/demo.mp4&t=file")
            .unwrap();

        assert_eq!(activation.matched_server.as_ref().unwrap().id, profile.id);
        assert_eq!(activation.browse_location.remote_path, "/Movies");
        assert_eq!(
            activation.browse_location.selected_path.as_deref(),
            Some("/Movies/demo.mp4")
        );
        assert_eq!(
            activation.preview_plan.as_ref().unwrap().content_type,
            PreviewContentType::Video
        );
        assert_eq!(
            activation
                .preview_plan
                .as_ref()
                .unwrap()
                .thumbnail
                .as_ref()
                .unwrap()
                .kind,
            PreviewKind::VideoPoster
        );
    }

    #[test]
    fn activates_directory_link_without_preview() {
        let store = CoreStore::in_memory().unwrap();
        let session = CoreSession::new(store);
        let target = QuickLinkTarget::new("nas", "Share", "/Projects", None, LinkKind::Directory);

        let activation = session.activate_target(target).unwrap();

        assert!(activation.matched_server.is_none());
        assert_eq!(activation.browse_location.remote_path, "/Projects");
        assert!(activation.browse_location.selected_path.is_none());
        assert!(activation.preview_plan.is_none());
    }
}
