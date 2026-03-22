"use client";

import React, { useState } from "react";
import { Card, CardBody, CardHeader, Col, Container, Row } from "reactstrap";
import Breadcrumbs from "@/Component/CommonElements/Breadcrumbs";
import Toastify from "toastify-js";
import "toastify-js/src/toastify.css";
import * as allCountryCodes from "country-flag-icons/react/1x1";
import { FlagData } from "@/app/icons/(flag)/_components/FlagData";
import { IconIcons } from "@tabler/icons-react";

type FlagComponents = typeof allCountryCodes;
type CountryCode = keyof FlagComponents;
const flagComponents: Record<CountryCode, React.ComponentType> =
  allCountryCodes;

const FlagIcons: React.FC = () => {
  const [searchValue, setSearchValue] = useState<string>("");

  const countryCodes = Object.keys(flagComponents) as CountryCode[];

  // Apply search filtering (only 2-letter codes, and match searchValue)
  const filteredCountries = countryCodes
    .filter((code) => code.length === 2)
    .filter((code) => code.toLowerCase().includes(searchValue.toLowerCase()));

  const copyCode = (countryCode: string) => {
    const flagTag = `<${countryCode} className="w-16 h-12" />`;
    navigator.clipboard.writeText(flagTag);
    Toastify({
      text: "Copied to clipboard successfully!",
      duration: 3000,
      close: true,
      gravity: "top",
      position: "right",
      stopOnFocus: true,
      style: {
        background: "rgba(var(--success),1)",
      },
    }).showToast();
  };

  return (
    <Container fluid>
      <Breadcrumbs
        mainTitle="Flags"
        title="Icons"
        path={["Flags"]}
        Icon={IconIcons}
      />
      <Row>
        <Col xs="12">
          <Card>
            <CardHeader>
              <Row>
                <Col md="4">
                  <div className="search-bar app-form app-icon-form position-relative">
                    <input
                      type="search"
                      className="form-control"
                      placeholder="Type country code (e.g., us, gb)"
                      value={searchValue}
                      onChange={(e) => setSearchValue(e.target.value)}
                    />
                  </div>
                </Col>
              </Row>
            </CardHeader>
            <CardBody className="d-flex justify-content-between align-items-center">
              <ul className="icon-list space-top-icon">
                {filteredCountries.map((countryCode) => {
                  const FlagComponent = flagComponents[countryCode]!;
                  const flagLabel =
                    FlagData[countryCode as keyof typeof FlagData] ??
                    countryCode;
                  return (
                    <li
                      key={countryCode}
                      className="icon-box pb-33"
                      onClick={() => copyCode(countryCode)}
                    >
                      <FlagComponent />
                      <div className="icon-box-codes d-flex flex-column">
                        <strong className="text-uppercase">
                          {countryCode}
                        </strong>
                        <code>{flagLabel}</code>
                      </div>
                    </li>
                  );
                })}
              </ul>
            </CardBody>
          </Card>
        </Col>
      </Row>
    </Container>
  );
};

export default FlagIcons;
