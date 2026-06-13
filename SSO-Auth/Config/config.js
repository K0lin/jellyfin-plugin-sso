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
      const response = await fetch(
        ApiClient.getUrl("web/configurationpage") +
          `?name=SSO-Auth-i18n-${locale}.json`,
      );
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
    document.title = this.t("config.PageTitle");
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

    view.querySelectorAll("[data-i18n-title]").forEach((element) => {
      element.title = this.t(element.getAttribute("data-i18n-title"));
    });
  },
};

const ssoConfigurationPage = {
  pluginUniqueId: "505ce9d1-d916-42fa-86ca-673ef241d7df",
  loadConfiguration: (page) => {
    ApiClient.getPluginConfiguration(ssoConfigurationPage.pluginUniqueId).then(
      (config) => {
        ssoConfigurationPage.populateProviders(page, config.OidConfigs);
      },
    );

    const folder_container = page.querySelector("#EnabledFolders");
    ssoConfigurationPage.populateFolders(folder_container);
  },
  populateProviders: (page, providers) => {
    // Clear providers in case there are out of date ones
    page
      .querySelector("#selectProvider")
      .querySelectorAll("option")
      .forEach((option) => {
        option.remove();
      });

    // Add providers as options for the selector

    Object.keys(providers).forEach((provider_name) => {
      var choice = new Option(provider_name, provider_name);

      page.querySelector("#selectProvider").appendChild(choice);
    });
  },
  populateEnabledFolders: (folder_list, container) => {
    container.querySelectorAll(".folder-checkbox").forEach((e) => {
      e.checked = folder_list.includes(e.getAttribute("data-id"));
    });
  },
  serializeEnabledFolders: (container) => {
    return [...container.querySelectorAll(".folder-checkbox")]
      .filter((e) => e.checked)
      .map((e) => {
        return e.getAttribute("data-id");
      });
  },
  populateFolders: (container) => {
    return ApiClient.getJSON(
      ApiClient.getUrl("Library/MediaFolders", {
        IsHidden: false,
      }),
    ).then((folders) => {
      ssoConfigurationPage._populateFolders(container, folders);
    });
  },
  /*
  container: html element
  folders.Items: array of objects, with .Id & .Name
  */
  _populateFolders: (container, folders) => {
    const checkboxes = folders.Items.map((folder) => {
      var out = document.createElement("label");
      out.classList.add("sso-folder-checkbox-label");

      out.innerHTML = `
        <input
          is="emby-checkbox"
          class="folder-checkbox chkFolder"
          data-id="${folder.Id}"
          type="checkbox"
        />
        <span>${folder.Name}</span>
      `;

      return out;
    });

    container.replaceChildren(...checkboxes);
  },

  populateRoleMappings: (folder_role_mappings, container) => {
    container
      .querySelectorAll(".sso-role-mapping-container")
      .forEach((e) => e.remove());

    const mapping_elements = folder_role_mappings.map((mapping) => {
      var elem = document.createElement("div");

      elem.classList.add("sso-role-mapping-container");
      elem.innerHTML = `
      <label
        class="inputLabel inputLabelUnfocused sso-role-mapping-input-label" 
      >${ssoI18n.t("config.fields.RoleMappingRoleLabel")}</label>
      <div class="listItem">
        <input
          is="emby-input"
          required=""
          type="text"
          class="listItemBody sso-role-mapping-name"
        />
        <button
          type="button"
          is="paper-icon-button-light"
          class="listItemButton sso-remove-role-mapping"
        >
          <span class="material-icons remove_circle" aria-hidden="true"></span>
        </button> 
      </div> 
      <div
        class="checkboxList paperList sso-folder-list"
      ></div>
      `;

      var checklist = elem.querySelector(".sso-folder-list");
      const enabled_folders = mapping["Folders"];

      ssoConfigurationPage
        .populateFolders(checklist)
        .then(() =>
          ssoConfigurationPage.populateEnabledFolders(
            enabled_folders,
            checklist,
          ),
        );

      elem.querySelector(".sso-role-mapping-name").value = mapping["Role"];
      elem
        .querySelector(".sso-remove-role-mapping")
        .addEventListener(
          "click",
          ssoConfigurationPage.handleRoleMappingRemove,
        );

      return elem;
    });

    mapping_elements.forEach((e) => container.appendChild(e));
  },
  serializeRoleMappings: (container) => {
    var out = [];
    const roles = [
      ...container.querySelectorAll(".sso-role-mapping-container"),
    ].forEach((elem) => {
      const role = elem.querySelector(".sso-role-mapping-name").value;
      const checklist = elem.querySelector(".sso-folder-list");

      out.push({
        Role: role,
        Folders: ssoConfigurationPage.serializeEnabledFolders(checklist),
      });
    });

    return out;
  },
  handleRoleMappingRemove: (evt) => {
    const targeted_mapping = evt.target.closest(".sso-role-mapping-container");
    targeted_mapping.remove();
  },
  listArgumentsByType: (page) => {
    const json_class = ".sso-json";
    const toggle_class = ".sso-toggle";
    const text_class = ".sso-text";
    const text_list_class = ".sso-line-list";

    const folder_list_fields = ["EnabledFolders"];
    const role_map_fields = ["FolderRoleMapping"];

    const oidc_form = page.querySelector("#sso-new-oidc-provider");

    const text_fields = [...oidc_form.querySelectorAll(text_class)].map(
      (e) => e.id,
    );

    const json_fields = [...oidc_form.querySelectorAll(json_class)].map(
      (e) => e.id,
    );

    const text_list_fields = [
      ...oidc_form.querySelectorAll(text_list_class),
    ].map((e) => e.id);

    const check_fields = [...oidc_form.querySelectorAll(toggle_class)].map(
      (e) => e.id,
    );

    const output = {
      json_fields,
      text_list_fields,
      text_fields,
      check_fields,
      folder_list_fields,
      role_map_fields,
    };

    return output;
  },
  fillTextList: (text_list, element) => {
    // text_list is an array of strings
    // element is an input element
    const val = text_list.join("\r\n");
    element.value = val;
  },
  parseTextList: (element) => {
    // Return the parsed text list
    var out = element.value
      .split("\n")
      .map((e) => e.trim())
      .filter((e) => e);
    return out;
  },
  loadProvider: (page, provider_name) => {
    ApiClient.getPluginConfiguration(ssoConfigurationPage.pluginUniqueId).then(
      (config) => {
        var provider = config.OidConfigs[provider_name] || {};

        const form_elements = ssoConfigurationPage.listArgumentsByType(page);

        page.querySelector("#OidProviderName").value = provider_name;

        form_elements.text_fields.forEach((id) => {
          if (provider[id]) page.querySelector("#" + id).value = provider[id];
        });

        form_elements.json_fields.forEach((id) => {
          if (provider[id])
            page.querySelector("#" + id).value = JSON.stringify(provider[id]);
        });

        form_elements.text_list_fields.forEach((id) => {
          if (provider[id])
            ssoConfigurationPage.fillTextList(
              provider[id],
              page.querySelector("#" + id),
            );
        });

        form_elements.folder_list_fields.forEach((id) => {
          if (provider[id]) {
            ssoConfigurationPage.populateEnabledFolders(
              provider[id],
              page.querySelector(`#${id}`),
            );
          }
        });

        form_elements.check_fields.forEach((id) => {
          if (provider[id]) page.querySelector("#" + id).checked = provider[id];
        });

        form_elements.role_map_fields.forEach((id) => {
          const elem = page.querySelector(`#${id}`);
          if (provider[id])
            ssoConfigurationPage.populateRoleMappings(provider[id], elem);
        });
      },
    );
  },
  deleteProvider: (page, provider_name) => {
    if (
      !window.confirm(
        ssoI18n.t("config.messages.DeleteProviderConfirm", provider_name),
      )
    ) {
      return;
    }
    return new Promise((resolve) => {
      ApiClient.getPluginConfiguration(
        ssoConfigurationPage.pluginUniqueId,
      ).then((config) => {
        if (!config.OidConfigs.hasOwnProperty(provider_name)) {
          resolve();
          return;
        }

        delete config.OidConfigs[provider_name];
        ApiClient.updatePluginConfiguration(
          ssoConfigurationPage.pluginUniqueId,
          config,
        ).then(function (result) {
          Dashboard.processPluginConfigurationUpdateResult(result);
          ssoConfigurationPage.loadConfiguration(page);

          Dashboard.alert(ssoI18n.t("config.messages.ProviderRemoved"));

          resolve();
        });
      });
    });
  },
  saveProvider: (page, provider_name) => {
    return new Promise((resolve) => {
      const form_elements = ssoConfigurationPage.listArgumentsByType(page);

      ApiClient.getPluginConfiguration(
        ssoConfigurationPage.pluginUniqueId,
      ).then((config) => {
        var current_config = {};
        if (config.OidConfigs.hasOwnProperty(provider_name)) {
          current_config = config.OidConfigs[provider_name];
        }

        form_elements.text_fields.forEach((id) => {
          const value = page.querySelector("#" + id).value;
          if (value) {
            current_config[id] = page.querySelector("#" + id).value;
          } else {
            current_config[id] = null;
          }
        });

        form_elements.json_fields.forEach((id) => {
          const value = page.querySelector("#" + id).value;
          if (value) {
            current_config[id] = JSON.parse(value);
          } else {
            current_config[id] = null;
          }
        });

        form_elements.check_fields.forEach((id) => {
          current_config[id] = page.querySelector("#" + id).checked;
        });

        form_elements.text_list_fields.forEach((id) => {
          current_config[id] = ssoConfigurationPage.parseTextList(
            page.querySelector("#" + id),
          );
        });

        form_elements.folder_list_fields.forEach((id) => {
          const elem = page.querySelector(`#${id}`);
          current_config[id] =
            ssoConfigurationPage.serializeEnabledFolders(elem);
        });

        form_elements.role_map_fields.forEach((id) => {
          const elem = page.querySelector(`#${id}`);
          current_config[id] = ssoConfigurationPage.serializeRoleMappings(elem);
        });

        config.OidConfigs[provider_name] = current_config;

        ApiClient.updatePluginConfiguration(
          ssoConfigurationPage.pluginUniqueId,
          config,
        ).then(function (result) {
          Dashboard.processPluginConfigurationUpdateResult(result);
          ssoConfigurationPage.loadConfiguration(page);
          ssoConfigurationPage.loadProvider(page, provider_name);

          page.querySelector("#selectProvider").value = provider_name;
          Dashboard.alert(ssoI18n.t("config.messages.SettingsSaved"));
          resolve();
        });
      });
    });
  },
  addTextAreaStyle: (view) => {
    if (view.querySelector("#sso-config-style")) {
      return;
    }

    var style = document.createElement("link");
    style.id = "sso-config-style";
    style.rel = "stylesheet";
    style.href =
      ApiClient.getUrl("web/configurationpage") + "?name=SSO-Auth.css";
    view.appendChild(style);
  },

  bindEvents: (view) => {
    if (view.dataset.ssoConfigEventsBound === "true") {
      return;
    }

    view.dataset.ssoConfigEventsBound = "true";

    view.querySelector("#SaveProvider").addEventListener("click", (e) => {
      const target_provider = view.querySelector("#OidProviderName").value;

      ssoConfigurationPage.saveProvider(view, target_provider);

      e.preventDefault();
      return false;
    });

    view.querySelector("#LoadProvider").addEventListener("click", (e) => {
      const target_provider = view.querySelector("#selectProvider").value;

      ssoConfigurationPage.loadProvider(view, target_provider);

      e.preventDefault();
      return false;
    });

    view.querySelector("#DeleteProvider").addEventListener("click", (e) => {
      const target_provider = view.querySelector("#selectProvider").value;

      ssoConfigurationPage.deleteProvider(view, target_provider);

      e.preventDefault();
      return false;
    });

    view.querySelector("#AddRoleMapping").addEventListener("click", (e) => {
      const container = view.querySelector("#FolderRoleMapping");
      const current_mappings =
        ssoConfigurationPage.serializeRoleMappings(container);
      current_mappings.push({ Role: "", Folders: [] });
      console.log(current_mappings);
      ssoConfigurationPage.populateRoleMappings(current_mappings, container);

      e.preventDefault();
      return false;
    });
  },
};

export default function (view) {
  ssoConfigurationPage.addTextAreaStyle(view);
  ssoConfigurationPage.bindEvents(view);

  view.querySelector("#sso-self-service-link").href =
    ApiClient.getUrl("/SSOViews/linking");

  ssoI18n.load().then(() => {
    ssoI18n.localize(view);
    ssoConfigurationPage.loadConfiguration(view);
    ssoConfigurationPage.listArgumentsByType(view);
  });
}
