"use client";
import React from "react";
import "prismjs/themes/prism.css";
import { Container, Row } from "reactstrap";
import Breadcrumbs from "@/Component/CommonElements/Breadcrumbs";
import BasicButtons from "@/app/ui-kit/(buttons)/_components/BasicButtons";
import IconButton from "@/app/ui-kit/(buttons)/_components/IconButton";
import RadiusButton from "@/app/ui-kit/(buttons)/_components/RadiusButton";
import IconSocialButton from "@/app/ui-kit/(buttons)/_components/IconSocialButton";
import DisabledButton from "@/app/ui-kit/(buttons)/_components/DisabledButton";
import ActiveButton from "@/app/ui-kit/(buttons)/_components/ActiveButton";
import LoadingButton from "@/app/ui-kit/(buttons)/_components/LoadingButton";
import BlockButton from "@/app/ui-kit/(buttons)/_components/BlockButton";
import SizeButton from "@/app/ui-kit/(buttons)/_components/SizeButton";
import SizeRadiusButton from "@/app/ui-kit/(buttons)/_components/SizeRadiusButton";
import ButtonGroup from "@/app/ui-kit/(buttons)/_components/ButtonGroup";
import NestingButton from "@/app/ui-kit/(buttons)/_components/NestingButton";
import VerticalButton from "@/app/ui-kit/(buttons)/_components/VerticalButton";
import SocialButtons from "@/app/ui-kit/(buttons)/_components/SocialButton";
import { IconBriefcase } from "@tabler/icons-react";
import PrismCodeWrapper from "@/Component/CommonElements/PrismCodeWrapper";

const ButtonsPage = () => {
  return (
    <div>
      <Container fluid>
        <Breadcrumbs
          mainTitle="Buttons"
          title="Ui Kits"
          path={["Buttons"]}
          Icon={IconBriefcase}
        />

        <PrismCodeWrapper>
          <Row>
            <BasicButtons />
            <ButtonGroup />
            <IconButton />
            <RadiusButton />
            <SocialButtons />
            <IconSocialButton />
            <DisabledButton />
            <ActiveButton />
            <LoadingButton />
            <BlockButton />
            <SizeButton />
            <SizeRadiusButton />
            <NestingButton />
            <VerticalButton />
          </Row>
        </PrismCodeWrapper>
      </Container>
    </div>
  );
};

export default ButtonsPage;
