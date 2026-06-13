const normalizeLocale = (locale) =>
  (locale || "en-us").toLowerCase().replace("_", "-");

const ssoI18n = {
  locale: "en-us",
  strings: {},
  supportedLocales: ["en-us"],

  detectLocale() {
    const browserLocales = navigator.languages?.length
      ? navigator.languages
      : [navigator.language];
    const normalizedLocales = browserLocales.map(normalizeLocale);

    this.locale =
      normalizedLocales.find((locale) =>
        this.supportedLocales.includes(locale),
      ) ||
      normalizedLocales
        .map((locale) => locale.split("-")[0])
        .map((language) =>
          this.supportedLocales.find(
            (supportedLocale) => supportedLocale.split("-")[0] === language,
          ),
        )
        .find(Boolean) ||
      "en-us";

    document.documentElement.lang = this.locale;
  },

  async load() {
    this.detectLocale();

    const loadLocale = async (locale) => {
      const response = await fetch(`./i18n/${locale}.json`);
      if (!response.ok) {
        throw new Error(`Unable to load locale ${locale}`);
      }

      return response.json();
    };

    try {
      this.strings = await loadLocale(this.locale);
    } catch (error) {
      if (this.locale !== "en-us") {
        this.locale = "en-us";
        this.strings = await loadLocale(this.locale);
      } else {
        console.warn(error);
        this.strings = {};
      }
    }

    document.documentElement.lang = this.locale;
    document.title = this.t("linking.PageTitle");
  },

  t(key, ...args) {
    const value =
      key.split(".").reduce((current, part) => current?.[part], this.strings) ||
      key;

    return args.reduce(
      (text, arg, index) => text.replace(`{${index}}`, arg),
      value,
    );
  },

  localize(view) {
    view.querySelectorAll("[data-i18n]").forEach((element) => {
      element.textContent = this.t(element.getAttribute("data-i18n"));
    });
  },
};

const ssoConfigLinking = {
  pluginUniqueId: "505ce9d1-d916-42fa-86ca-673ef241d7df",
  loadProviders: (view) => {
    const provider_list_id = "sso-provider-list";
    const provider_list_saml_id = `${provider_list_id}-saml`;
    const provider_list_oid_id = `${provider_list_id}-oid`;

    const provider_list_saml = view.querySelector(`#${provider_list_saml_id}`);
    const provider_list_oid = view.querySelector(`#${provider_list_oid_id}`);
    provider_list_saml.innerHTML = "";
    provider_list_oid.innerHTML = "";

    fetch(new Request(ApiClient.getUrl("sso/OID/GetNames"))).then((resp) => {
      resp.json().then((config_names) => {
        ssoConfigLinking.loadProviderList(
          provider_list_oid,
          config_names,
          "oid",
        );
      });
    });
    fetch(new Request(ApiClient.getUrl("sso/SAML/GetNames"))).then((resp) => {
      resp.json().then((config_names) => {
        ssoConfigLinking.loadProviderList(
          provider_list_saml,
          config_names,
          "saml",
        );
      });
    });
  },
  loadProviderList: (container, providers, provider_mode) => {
    if (!providers.length) {
      const emptyState = document.createElement("div");
      emptyState.classList.add("sso-empty-state");
      emptyState.textContent = ssoI18n.t(
        "linking.NoProvidersConfigured",
        provider_mode.toUpperCase(),
      );
      container.appendChild(emptyState);
      return;
    }

    providers.forEach((provider_name) => {
      var provider_config = document.createElement("div");
      provider_config.classList.add("sso-provider-links-container");
      provider_config.setAttribute("data-id", provider_name);

      const heading = document.createElement("div");
      heading.classList.add("sso-provider-card-heading");

      const title = document.createElement("div");
      title.classList.add("sso-provider-link-title");
      title.textContent = provider_name;

      const mode = document.createElement("span");
      mode.classList.add("sso-provider-mode");
      mode.textContent = provider_mode.toUpperCase();

      heading.appendChild(title);
      heading.appendChild(mode);

      const linkContainer = document.createElement("div");
      linkContainer.classList.add("sso-provider-existing-links-container");
      linkContainer.setAttribute("data-provider", provider_name);

      const add_provider = document.createElement("a");
      add_provider.classList.add(
        "raised",
        "emby-button",
        "sso-provider-add-link",
        "sso-provider",
      );
      add_provider.innerHTML = `<span class="material-icons add" aria-hidden="true"></span><span>${ssoI18n.t("linking.LinkAccount")}</span>`;

      add_provider.href = ApiClient.getUrl(
        `/SSO/${provider_mode}/p/${provider_name}?isLinking=true`,
      );

      provider_config.appendChild(heading);
      provider_config.appendChild(linkContainer);
      provider_config.appendChild(add_provider);
      container.appendChild(provider_config);
    });

    const currentUserId = ApiClient.getCurrentUserId();

    if (currentUserId) {
      ApiClient.fetch(
        {
          type: "GET",
          url: ApiClient.getUrl(`sso/${provider_mode}/links/${currentUserId}`),
        },
        true,
      ).then((resp) => {
        resp.json().then((provider_map) => {
          console.log({ provider_map, currentUserId });

          Object.keys(provider_map).forEach((provider_name) => {
            const provider_container = [
              ...container.querySelectorAll(
                ".sso-provider-existing-links-container",
              ),
            ].find(
              (elem) => elem.getAttribute("data-provider") === provider_name,
            );
            ssoConfigLinking.populateExistingLinks(
              provider_container,
              provider_mode,
              provider_name,
              provider_map[provider_name],
            );
          });
        });
      });
    }
  },

  populateExistingLinks: (
    container,
    provider_mode,
    provider_name,
    canonical_names,
  ) => {
    if (!container) return;

    container.replaceChildren();

    if (!canonical_names.length) {
      const emptyState = document.createElement("p");
      emptyState.classList.add("sso-linked-empty");
      emptyState.textContent = ssoI18n.t("linking.NoIdentityLinked");
      container.appendChild(emptyState);
      return;
    }

    const sectionTitle = document.createElement("p");
    sectionTitle.classList.add("sso-linked-title");
    sectionTitle.textContent = ssoI18n.t("linking.LinkedIdentities");
    container.appendChild(sectionTitle);

    const checkboxes = canonical_names.map((canonical_name) => {
      var out = document.createElement("label");
      out.classList.add("sso-provider-link-checkbox-wrapper");

      const input = document.createElement("input");
      input.classList.add("sso-link-checkbox");
      input.setAttribute("data-id", canonical_name);
      input.setAttribute("data-mode", provider_mode);
      input.setAttribute("data-provider", provider_name);
      input.type = "checkbox";

      const label = document.createElement("span");
      label.classList.add("sso-provider-link-checkbox-text");
      label.textContent = canonical_name;

      out.appendChild(input);
      out.appendChild(label);
      return out;
    });

    checkboxes.forEach((e) => {
      container.appendChild(e);
    });
  },

  handleDeleteButtonPressed: (evt, view) => {
    if (evt.target.disabled) return;

    const currentUserId = ApiClient.getCurrentUserId();
    if (!currentUserId) return;

    const delete_requests = [...view.querySelectorAll(".sso-link-checkbox")]
      .filter((checkbox_link) => {
        const canonical_name = checkbox_link.getAttribute("data-id");
        const provider_name = checkbox_link.getAttribute("data-provider");
        const provider_mode = checkbox_link.getAttribute("data-mode");

        if (![canonical_name, provider_name, provider_mode].every((e) => e)) {
          return false;
        }

        if (!checkbox_link.checked) {
          return false;
        }

        return true;
      })
      .map((checked_link) => {
        const canonical_name = checked_link.getAttribute("data-id");
        const provider_name = checked_link.getAttribute("data-provider");
        const provider_mode = checked_link.getAttribute("data-mode");

        return ApiClient.fetch({
          type: "DELETE",
          url: ApiClient.getUrl(
            `sso/${provider_mode}/link/${provider_name}/${currentUserId}/${canonical_name}`,
          ),
        });
      });

    Promise.all(delete_requests).then((values) => {
      console.log({ message: "Delete requests handled", values });
      window.location.reload();
    });
  },
};

export default async function (view) {
  await ssoI18n.load();
  ssoI18n.localize(view);
  ssoConfigLinking.loadProviders(view);

  view.querySelector("#enable-delete").addEventListener("change", (e) => {
    view.querySelector("#btn-delete-selected-links").disabled =
      !e.target.checked;
  });

  view
    .querySelector("#btn-delete-selected-links")
    .addEventListener("click", (e) =>
      ssoConfigLinking.handleDeleteButtonPressed(e, view),
    );
}
