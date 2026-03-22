"use client";

import React, { useEffect, useState } from "react";
import { Card, CardBody, CardHeader, Col, Container, Row } from "reactstrap";
import { Icon } from "@iconify/react";
import Toastify from "toastify-js";
import Breadcrumbs from "@/Component/CommonElements/Breadcrumbs";
import "toastify-js/src/toastify.css";
import { IconIcons } from "@tabler/icons-react";

const fetchLineMDIcons = async (): Promise<string[][]> => {
  try {
    const response = await fetch(
      "https://api.iconify.design/collection?prefix=line-md&chars=true&aliases=true"
    );
    const data = await response.json();
    const categories = Object.keys(data.categories);
    return categories.map((icon) =>
      data.categories[icon].map((item: string[]) => item)
    ) as string[][];
  } catch (error) {
    return [];
  }
};

const AnimatedPage: React.FC = () => {
  const [originalList, setOriginalList] = useState<string[][]>([]);
  const [iconList, setIconList] = useState<string[][]>([]);
  const [searchValue, setSearchValue] = useState<string>("");

  // Fetch icons on mount
  useEffect(() => {
    const fetchIcons = async () => {
      const icons: string[][] = await fetchLineMDIcons();
      setOriginalList(icons);
      setIconList(icons);
    };

    fetchIcons();
  }, []);

  useEffect(() => {
    if (searchValue.trim() === "") {
      setIconList(originalList);
    } else {
      setIconList(
        originalList.map((iconData) =>
          iconData.filter((icon) => {
            return icon.toLowerCase().includes(searchValue.toLowerCase());
          })
        )
      );
    }
  }, [searchValue, originalList]);

  const copyIconToClipboard = (iconName: string) => {
    navigator.clipboard.writeText(
      `<iconify-icon icon="${iconName}"></iconify-icon>`
    );

    Toastify({
      text: "Copied to clipboard successfully",
      duration: 3000,
      close: true,
      gravity: "top",
      position: "right",
      stopOnFocus: true,
      style: { background: "rgba(var(--success),1)" },
    }).showToast();
  };

  return (
    <Container fluid>
      <Breadcrumbs
        mainTitle="Animated"
        title="Icons"
        path={["Animated"]}
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
                      name="search"
                      placeholder="type to search"
                      onChange={(e) => setSearchValue(e.target.value)}
                    />
                  </div>
                </Col>
                <Col md="8" className="text-end pt-2" />
              </Row>
            </CardHeader>
            <CardBody>
              <ul className="icon-list">
                {iconList.map((iconData, dataIndex) =>
                  iconData.map((icon, iconIndex) => {
                    const iconKey = `${dataIndex}-${iconIndex}`;
                    return (
                      <li
                        className={"icon-box"}
                        onClick={() => copyIconToClipboard(icon)}
                        key={iconKey}
                        data-icon={icon}
                      >
                        <i>
                          <Icon
                            icon={`line-md:${icon}`}
                            width={30}
                            height={30}
                          />
                        </i>
                        <strong className="mb-3">{icon}</strong>
                      </li>
                    );
                  })
                )}
              </ul>
            </CardBody>
          </Card>
        </Col>
      </Row>
    </Container>
  );
};

export default AnimatedPage;
