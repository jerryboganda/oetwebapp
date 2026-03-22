"use client";
import React from "react";
import { Container, Row } from "reactstrap";
import "prismjs/themes/prism.css";
import Breadcrumbs from "@/Component/CommonElements/Breadcrumbs";
import ColorAvatar from "@/app/ui-kit/(avatar)/_components/ColorAvatar";
import ImagesAvatar from "@/app/ui-kit/(avatar)/_components/ImagesAvatar";
import LightAvatar from "@/app/ui-kit/(avatar)/_components/LightAvatar";
import OutlineAvatar from "@/app/ui-kit/(avatar)/_components/OutlineAvatar";
import RadiusAvatar from "@/app/ui-kit/(avatar)/_components/RadiusAvatar";
import TooltipAvatar from "@/app/ui-kit/(avatar)/_components/TooltipAvatar";
import { IconBriefcase } from "@tabler/icons-react";
import PrismCodeWrapper from "@/Component/CommonElements/PrismCodeWrapper";

const AvatarPage = () => {
  return (
    <div>
      <Container fluid>
        <Breadcrumbs
          mainTitle="Avatar"
          title="Ui Kits"
          path={["Avatar"]}
          Icon={IconBriefcase}
        />
        <Row>
          <PrismCodeWrapper>
            <ColorAvatar />
            <LightAvatar />
            <RadiusAvatar />
            <OutlineAvatar />
            <ImagesAvatar />
            <TooltipAvatar />
          </PrismCodeWrapper>
        </Row>
      </Container>
    </div>
  );
};

export default AvatarPage;
