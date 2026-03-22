import React, { useState } from "react";
import {
  UncontrolledDropdown,
  DropdownToggle,
  DropdownMenu,
  DropdownItem,
} from "reactstrap";
import US from "country-flag-icons/react/1x1/US";
import FR from "country-flag-icons/react/1x1/FR";
import GB from "country-flag-icons/react/1x1/GB";
import RU from "country-flag-icons/react/1x1/RU";
import IT from "country-flag-icons/react/1x1/IT";

const flagComponents: Record<string, React.ElementType> = {
  en: US,
  fr: FR,
  uk: GB,
  ru: RU,
  it: IT,
};

interface Language {
  code: keyof typeof flagComponents;
  label: string;
  title: string;
}

const languages: Language[] = [
  {
    code: "en",
    label: "English (US)",
    title: "US",
  },
  {
    code: "fr",
    label: "Français",
    title: "FR",
  },
  {
    code: "uk",
    label: "English (UK)",
    title: "UK",
  },
  {
    code: "ru",
    label: "Русский",
    title: "RU",
  },
  {
    code: "it",
    label: "Italiano",
    title: "IT",
  },
];

const LanguageSelector: React.FC = () => {
  const [selectedLang, setSelectedLang] =
    useState<keyof typeof flagComponents>("en");
  const FlagIcon = flagComponents[selectedLang] || US; // Fallback to US flag if not found

  return (
    <UncontrolledDropdown direction="down">
      <DropdownToggle tag="a" className="d-block head-icon ps-0" role="button">
        <div className={`lang-flag lang-${selectedLang}`}>
          <span className="flag rounded-circle overflow-hidden">
            <FlagIcon
              title={selectedLang.toUpperCase()}
              className="w-25 h-25 rounded-circle overflow-hidden"
            />
          </span>
        </div>
      </DropdownToggle>

      <DropdownMenu className="language-dropdown header-card border-0">
        {languages.map(({ code, label, title }) => {
          return (
            <DropdownItem
              key={code}
              className={`lang lang-${code} dropdown-item p-2 ${
                selectedLang === code ? "selected" : ""
              }`}
              title={title}
              onClick={() => setSelectedLang(code)}
            >
              <span className="d-flex align-items-center">
                {(() => {
                  const FlagComponent = flagComponents[code];
                  return FlagComponent ? (
                    <FlagComponent
                      title={title}
                      className="w-22 h-22 b-r-8 mr-8"
                    />
                  ) : null;
                })()}
                <span className="ps-1">{label}</span>
              </span>
            </DropdownItem>
          );
        })}
      </DropdownMenu>
    </UncontrolledDropdown>
  );
};

export default LanguageSelector;
