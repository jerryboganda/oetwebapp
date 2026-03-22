"use client";
import React from "react";
import "prismjs/themes/prism.css";
import { Container, Row } from "reactstrap";
import Breadcrumbs from "@/Component/CommonElements/Breadcrumbs";
import BasicDropdown from "@/app/ui-kit/(dropdown)/_components/BasicDropdown";
import ColorDropdown from "@/app/ui-kit/(dropdown)/_components/ColorDropdown";
import OutlineDropdown from "@/app/ui-kit/(dropdown)/_components/OutlineDropdown";
import SolidDropdown from "@/app/ui-kit/(dropdown)/_components/SolidDropdown";
import LightDropdown from "@/app/ui-kit/(dropdown)/_components/LightDropdown";
import DropUpDropdown from "@/app/ui-kit/(dropdown)/_components/DropUpDropdown";
import { IconBriefcase } from "@tabler/icons-react";
import PrismCodeWrapper from "@/Component/CommonElements/PrismCodeWrapper";

const DropdownPage = () => {
  return (
    <>
      <Container fluid className="dropdown-section">
        <Breadcrumbs
          mainTitle="Dropdown"
          title="Ui Kits"
          path={["Dropdown"]}
          Icon={IconBriefcase}
        />

        <PrismCodeWrapper>
          <Row>
            <BasicDropdown />
            <SolidDropdown />
            <OutlineDropdown />
            <LightDropdown />
            <DropUpDropdown />
            <ColorDropdown />
          </Row>
        </PrismCodeWrapper>
      </Container>
    </>
  );
};

export default DropdownPage;
